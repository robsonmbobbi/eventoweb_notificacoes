using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace EventoWebNotificacoes;

public class RabbitMqRpcServerService : BackgroundService
{
    private readonly ILogger<RabbitMqRpcServerService> _logger;
    private readonly RabbitMqOptions _options;
    private readonly INotificadorResolver _resolver;

    private static readonly TimeSpan MinRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    public RabbitMqRpcServerService(
        ILogger<RabbitMqRpcServerService> logger,
        IOptions<RabbitMqOptions> options,
        INotificadorResolver resolver)
    {
        _logger = logger;
        _options = options.Value;
        _resolver = resolver;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true
        };

        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // A fila é criada/gerenciada pela infra; este serviço apenas consome, não declara topologia.
        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: _options.PrefetchCount,
            global: false,
            cancellationToken: stoppingToken);

        var connectionLost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.ConnectionShutdownAsync += (_, _) =>
        {
            connectionLost.TrySetResult();
            return Task.CompletedTask;
        };

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) => OnMessageAsync(channel, ea);

        await channel.BasicConsumeAsync(
            queue: _options.RequestQueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation(
            "Consumindo fila '{Queue}' em {Host}:{Port}",
            _options.RequestQueueName, _options.HostName, _options.Port);

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
        finally
        {
            if (!string.IsNullOrEmpty(replyTo))
            {
                var replyProps = new BasicProperties { CorrelationId = correlationId };
                var body = JsonSerializer.SerializeToUtf8Bytes(response);

                await channel.BasicPublishAsync(
                    exchange: string.Empty,
                    routingKey: replyTo,
                    mandatory: false,
                    basicProperties: replyProps,
                    body: body);
            }
            else
            {
                _logger.LogWarning("Mensagem sem ReplyTo recebida, não é possível responder. Id={Id}", request?.Id);
            }

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
        }
    }
}
