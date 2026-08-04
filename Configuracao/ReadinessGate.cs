namespace EventoWebNotificacoes;

public class ReadinessGate
{
    private readonly object _lock = new();
    private readonly TaskCompletionSource _pronto = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool _rabbitMqValidado;
    private bool _emailValidado;
    private bool _whatsappValidado;

    public void AtualizarRabbitMq(bool ok) => Atualizar(validado: ok, campo: v => _rabbitMqValidado = v);
    public void AtualizarEmail(bool ok) => Atualizar(validado: ok, campo: v => _emailValidado = v);
    public void AtualizarWhatsapp(bool ok) => Atualizar(validado: ok, campo: v => _whatsappValidado = v);

    private void Atualizar(bool validado, Action<bool> campo)
    {
        lock (_lock)
        {
            campo(validado);
            if (_rabbitMqValidado && (_emailValidado || _whatsappValidado))
            {
                _pronto.TrySetResult();
            }
        }
    }

    public Task AguardarProntoAsync(CancellationToken ct) => _pronto.Task.WaitAsync(ct);

    public ReadinessStatusDto ObterStatus()
    {
        lock (_lock)
        {
            return new ReadinessStatusDto(_rabbitMqValidado, _emailValidado, _whatsappValidado, _pronto.Task.IsCompletedSuccessfully);
        }
    }
}

public record ReadinessStatusDto(bool RabbitMqValidado, bool EmailValidado, bool WhatsappValidado, bool Pronto);
