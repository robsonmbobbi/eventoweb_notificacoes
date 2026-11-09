using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventoWebNotificacoes;

/// <summary>
/// Valida as credenciais da WhatsApp Cloud API no boot, com retry/backoff em caso de falha, e
/// libera o <see cref="ReadinessGate"/> na primeira validação bem-sucedida. Assim um restart de uma
/// instância só-WhatsApp volta a consumir a fila sozinha.
/// </summary>
public class WhatsappValidacaoService : BackgroundService
{
    private static readonly TimeSpan MinRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);

    private readonly ILogger<WhatsappValidacaoService> _logger;
    private readonly WhatsappCloudClient _client;
    private readonly CanaisOptions _canais;
    private readonly ReadinessGate _gate;

    public WhatsappValidacaoService(
        ILogger<WhatsappValidacaoService> logger,
        WhatsappCloudClient client,
        IOptions<CanaisOptions> canais,
        ReadinessGate gate)
    {
        _logger = logger;
        _client = client;
        _canais = canais.Value;
        _gate = gate;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_canais.Whatsapp)
            return;

        var retryDelay = MinRetryDelay;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var info = await _client.ValidarAsync(stoppingToken);
                    _gate.MarcarWhatsappValidado();
                    _logger.LogInformation("Canal WhatsApp operacional: Cloud API validada para {Numero} ({Nome}), qualidade {Qualidade}.",
                        info.NumeroExibicao, info.NomeVerificado, info.Qualidade);
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex,
                        "Canal WhatsApp habilitado mas NÃO operacional: falha ao validar na Graph API ({Erro}). " +
                        "Consumo da fila bloqueado; nova tentativa em {Delay}s.",
                        ex.Message, retryDelay.TotalSeconds);
                }

                await Task.Delay(retryDelay, stoppingToken);
                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, MaxRetryDelay.TotalSeconds));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
