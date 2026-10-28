using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventoWebNotificacoes;

/// <summary>
/// Mantém o WhatsApp marcado como validado no <see cref="ReadinessGate"/> sem intervenção manual:
/// valida as credenciais na Graph API no boot e sempre que a configuração ou os canais mudam,
/// com retry/backoff em caso de falha. Assim um restart de uma instância só-WhatsApp volta a
/// consumir a fila sozinha, como acontecia com a reconexão automática do Baileys.
/// </summary>
public class WhatsappValidacaoService : BackgroundService
{
    private static readonly TimeSpan MinRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);

    private readonly ILogger<WhatsappValidacaoService> _logger;
    private readonly WhatsappCloudClient _client;
    private readonly ConfigStore _configStore;
    private readonly ReadinessGate _gate;

    private readonly SemaphoreSlim _validacaoLock = new(1, 1);
    private volatile TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile WhatsappValidacaoStatus _status = new(false, null, null, null);

    public WhatsappValidacaoService(
        ILogger<WhatsappValidacaoService> logger,
        WhatsappCloudClient client,
        ConfigStore configStore,
        ReadinessGate gate)
    {
        _logger = logger;
        _client = client;
        _configStore = configStore;
        _gate = gate;
    }

    public WhatsappValidacaoStatus Status => _status;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _configStore.CanaisChanged += OnConfigChanged;
        _configStore.WhatsappChanged += OnConfigChanged;
        try
        {
            var retryDelay = MinRetryDelay;

            while (!stoppingToken.IsCancellationRequested)
            {
                // Troca o TCS antes de validar: uma mudança que chegue durante a validação acorda o próximo ciclo.
                var wake = _wake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                var precisaValidar = _configStore.Canais.Whatsapp && _configStore.Whatsapp is { EstaCompleta: true };
                TimeSpan espera = Timeout.InfiniteTimeSpan;

                if (precisaValidar)
                {
                    if (await ValidarAgoraAsync(stoppingToken))
                    {
                        retryDelay = MinRetryDelay;
                    }
                    else
                    {
                        espera = retryDelay;
                        retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, MaxRetryDelay.TotalSeconds));
                    }
                }
                else
                {
                    _gate.AtualizarWhatsapp(false);
                    retryDelay = MinRetryDelay;
                }

                using var registration = stoppingToken.Register(() => wake.TrySetResult());
                await Task.WhenAny(Task.Delay(espera, stoppingToken), wake.Task);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _configStore.CanaisChanged -= OnConfigChanged;
            _configStore.WhatsappChanged -= OnConfigChanged;
        }
    }

    /// <summary>Valida as credenciais agora (usado também pelo botão "Validar" da UI).</summary>
    public async Task<bool> ValidarAgoraAsync(CancellationToken ct = default)
    {
        await _validacaoLock.WaitAsync(ct);
        try
        {
            var info = await _client.ValidarAsync(ct);
            _status = new WhatsappValidacaoStatus(true, info, null, DateTimeOffset.UtcNow);
            _gate.AtualizarWhatsapp(true);
            _logger.LogInformation("WhatsApp Cloud API validada: {Numero} ({Nome}), qualidade {Qualidade}.",
                info.NumeroExibicao, info.NomeVerificado, info.Qualidade);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _status = new WhatsappValidacaoStatus(false, null, ex.Message, DateTimeOffset.UtcNow);
            _gate.AtualizarWhatsapp(false);
            _logger.LogWarning(ex, "Falha ao validar a configuração da WhatsApp Cloud API.");
            return false;
        }
        finally
        {
            _validacaoLock.Release();
        }
    }

    private void OnConfigChanged() => _wake.TrySetResult();
    private void OnConfigChanged(CanaisOptions _) => _wake.TrySetResult();
}

public sealed record WhatsappValidacaoStatus(bool Valido, WhatsappNumeroInfo? Numero, string? Erro, DateTimeOffset? ValidadoEm);
