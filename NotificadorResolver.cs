namespace EventoWebNotificacoes;

public interface INotificadorResolver
{
    INotificador Resolver(EnumMeioNotificacao meio);
}

public sealed class NotificadorResolver : INotificadorResolver
{
    private readonly IReadOnlyDictionary<EnumMeioNotificacao, INotificador> _notificadores;

    public NotificadorResolver(IReadOnlyDictionary<EnumMeioNotificacao, INotificador> notificadores)
    {
        _notificadores = notificadores;
    }

    public INotificador Resolver(EnumMeioNotificacao meio) =>
        _notificadores.TryGetValue(meio, out var notificador)
            ? notificador
            : throw new NotSupportedException($"Meio de notificação não configurado neste worker: {meio}");
}
