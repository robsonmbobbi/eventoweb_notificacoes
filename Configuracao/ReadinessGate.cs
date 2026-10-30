using Microsoft.Extensions.Options;

namespace EventoWebNotificacoes;

/// <summary>
/// Segura o início do consumo da fila até o WhatsApp estar validado na Graph API (quando o canal
/// está habilitado). Email é validado no start (ValidateOnStart) e o RabbitMQ pelo próprio laço de
/// reconexão do <see cref="RabbitMqRpcServerService"/>, então nenhum dos dois passa por aqui.
/// </summary>
public class ReadinessGate
{
    private readonly TaskCompletionSource _pronto = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ReadinessGate(IOptions<CanaisOptions> canais)
    {
        if (!canais.Value.Whatsapp)
            _pronto.TrySetResult();
    }

    public bool EstaPronto => _pronto.Task.IsCompleted;

    public void MarcarWhatsappValidado() => _pronto.TrySetResult();

    public Task AguardarProntoAsync(CancellationToken ct) => _pronto.Task.WaitAsync(ct);
}
