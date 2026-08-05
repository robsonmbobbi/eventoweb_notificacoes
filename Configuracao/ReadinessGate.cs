namespace EventoWebNotificacoes;

public class ReadinessGate
{
    private readonly object _lock = new();
    private readonly TaskCompletionSource _pronto = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConfigStore _configStore;

    private bool _rabbitMqValidado;
    private bool _emailValidado;
    private bool _whatsappValidado;

    public ReadinessGate(ConfigStore configStore)
    {
        _configStore = configStore;
    }

    public void AtualizarRabbitMq(bool ok) => Atualizar(validado: ok, campo: v => _rabbitMqValidado = v);
    public void AtualizarEmail(bool ok) => Atualizar(validado: ok, campo: v => _emailValidado = v);
    public void AtualizarWhatsapp(bool ok) => Atualizar(validado: ok, campo: v => _whatsappValidado = v);

    private void Atualizar(bool validado, Action<bool> campo)
    {
        lock (_lock)
        {
            campo(validado);
            if (EstaPronto())
            {
                _pronto.TrySetResult();
            }
        }
    }

    // RabbitMQ é sempre obrigatório; dentre os canais habilitados nesta instância
    // (ConfigStore.Canais, lido ao vivo pois pode mudar em runtime pela UI), todos precisam
    // estar validados — não basta mais "pelo menos um", já que um canal desabilitado nunca
    // terá seu validado setado.
    private bool EstaPronto()
    {
        var canais = _configStore.Canais;
        return _rabbitMqValidado
            && (!canais.Email || _emailValidado)
            && (!canais.Whatsapp || _whatsappValidado);
    }

    // Usado só para o gate de início (RabbitMqRpcServerService espera "ficou pronto pela
    // primeira vez" antes de começar a consumir) — TaskCompletionSource é um latch que só liga
    // uma vez, de propósito: depois de começar a consumir, o serviço não deve parar de novo só
    // porque um canal foi desabilitado depois.
    public Task AguardarProntoAsync(CancellationToken ct) => _pronto.Task.WaitAsync(ct);

    public ReadinessStatusDto ObterStatus()
    {
        lock (_lock)
        {
            var canais = _configStore.Canais;
            return new ReadinessStatusDto(
                _rabbitMqValidado, _emailValidado, _whatsappValidado, EstaPronto(),
                canais.Email, canais.Whatsapp);
        }
    }
}

public record ReadinessStatusDto(
    bool RabbitMqValidado,
    bool EmailValidado,
    bool WhatsappValidado,
    bool Pronto,
    bool EmailHabilitado,
    bool WhatsappHabilitado);
