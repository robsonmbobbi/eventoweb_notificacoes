namespace EventoWebNotificacoes;

public interface INotificadorResolver
{
    INotificador Resolver(EnumMeioNotificacao meio);
}

public sealed class NotificadorResolver : INotificadorResolver
{
    private readonly ConfigStore _configStore;
    private readonly TemplateRenderer _renderer;
    private readonly WhatsappConnectionService _whatsappConnectionService;

    public NotificadorResolver(ConfigStore configStore, TemplateRenderer renderer, WhatsappConnectionService whatsappConnectionService)
    {
        _configStore = configStore;
        _renderer = renderer;
        _whatsappConnectionService = whatsappConnectionService;
    }

    public INotificador Resolver(EnumMeioNotificacao meio) => meio switch
    {
        EnumMeioNotificacao.EMail => ResolverEmail(),
        EnumMeioNotificacao.WhatsApp => new NotificadorWhatsapp(_renderer, _whatsappConnectionService),
        _ => throw new NotSupportedException($"Meio de notificação não configurado neste worker: {meio}")
    };

    private INotificador ResolverEmail()
    {
        var email = _configStore.Email;
        if (email is null ||
            string.IsNullOrWhiteSpace(email.ApiUrl) ||
            string.IsNullOrWhiteSpace(email.ApiKey) ||
            string.IsNullOrWhiteSpace(email.FromAddress))
        {
            throw new NotSupportedException("Meio de notificação não configurado neste worker: EMail");
        }

        return new NotificadorEmail(_renderer, email);
    }
}
