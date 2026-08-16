using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace EventoWebNotificacoes;

public class RabbitMqRpcServerService : BackgroundService
{
    private readonly ILogger<RabbitMqRpcServerService> _logger;
    private readonly ConfigStore _configStore;
    private readonly INotificadorResolver _resolver;
    private readonly ReadinessGate _gate;

    private static readonly TimeSpan MinRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    private volatile TaskCompletionSource? _connectionLost;

    public RabbitMqRpcServerService(
        ILogger<RabbitMqRpcServerService> logger,
        ConfigStore configStore,
        INotificadorResolver resolver,
        ReadinessGate gate)
    {
        _logger = logger;
        _configStore = configStore;
        _resolver = resolver;
        _gate = gate;
    }

    /// <summary>Corta a conexão atual imediatamente, forçando o loop de reconexão a reler a configuração.</summary>
    public void ForcarReconexao() => _connectionLost?.TrySetResult();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _gate.AguardarProntoAsync(stoppingToken);

        var retryDelay = MinRetryDelay;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
                retryDelay = MinRetryDelay;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Conexão com RabbitMQ perdida. Tentando novamente em {Delay}s.", retryDelay.TotalSeconds);

                try
                {
                    await Task.Delay(retryDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, MaxRetryDelay.TotalSeconds));
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        var options = _configStore.RabbitMq;

        var factory = new ConnectionFactory
        {
            HostName = options.HostName,
            Port = options.Port,
            UserName = options.UserName,
            Password = options.Password,
            VirtualHost = options.VirtualHost,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true
        };

        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            cancellationToken: stoppingToken);

        // A fila é criada/gerenciada pela infra; este serviço apenas consome, não declara topologia.
        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: options.PrefetchCount,
            global: false,
            cancellationToken: stoppingToken);

        var connectionLost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _connectionLost = connectionLost;
        connection.ConnectionShutdownAsync += (_, _) =>
        {
            connectionLost.TrySetResult();
            return Task.CompletedTask;
        };

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) => OnMessageAsync(channel, ea);

        await channel.BasicConsumeAsync(
            queue: options.RequestQueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation(
            "Consumindo fila '{Queue}' em {Host}:{Port}",
            options.RequestQueueName, options.HostName, options.Port);

        using var cancellationRegistration = stoppingToken.Register(() => connectionLost.TrySetResult());
        await connectionLost.Task;

        stoppingToken.ThrowIfCancellationRequested();
    }

    private async Task OnMessageAsync(IChannel channel, BasicDeliverEventArgs ea)
    {
        var correlationId = ea.BasicProperties.CorrelationId;
        var replyTo = ea.BasicProperties.ReplyTo;
        NotificacaoRequest? request = null;
        NotificacaoResponse response = new() { Sucesso = false, MensagemErro = "Erro desconhecido ao processar notificação." };

        try
        {
            var texto = System.Text.Encoding.UTF8.GetString(ea.Body.Span);
            request = JsonSerializer.Deserialize<NotificacaoRequest>(ea.Body.Span)
                      ?? throw new InvalidOperationException("Payload vazio ou inválido.");

            var notificador = _resolver.Resolver(request.Meio);
            await notificador.EnviarAsync(request);

            response = new NotificacaoResponse { Id = request.Id, Sucesso = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar notificação Id={Id}", request?.Id);
            response = new NotificacaoResponse { Id = request?.Id ?? Guid.Empty, Sucesso = false, MensagemErro = ex.Message };
        }

        if (string.IsNullOrEmpty(replyTo))
        {
            _logger.LogWarning("Mensagem sem ReplyTo recebida, não é possível responder. Id={Id}", request?.Id);
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            return;
        }

        // O cliente é responsável por declarar previamente uma fila própria, durável e de nome
        // fixo por instância, e informá-la em ReplyTo (não mais o amq.rabbitmq.reply-to efêmero).
        // Com publisher confirms + mandatory:true, BasicPublishAsync lança PublishException se a
        // resposta for nack'd ou não puder ser roteada (ex.: fila do cliente não existe) — só
        // confirmamos o pedido original (ack) depois de ter certeza de que a resposta foi
        // persistida pelo broker.
        try
        {
            var replyProps = new BasicProperties { CorrelationId = correlationId, Persistent = true };
            var body = JsonSerializer.SerializeToUtf8Bytes(response);

            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: replyTo,
                mandatory: true,
                basicProperties: replyProps,
                body: body);

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao publicar resposta em ReplyTo='{ReplyTo}' para Id={Id}; pedido não será ack'd e será reprocessado.", replyTo, response.Id);
        }
    }
}
