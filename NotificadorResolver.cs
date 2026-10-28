namespace EventoWebNotificacoes;

public interface INotificadorResolver
{
    INotificador Resolver(EnumMeioNotificacao meio);
}

public sealed class NotificadorResolver : INotificadorResolver
{
    private readonly ConfigStore _configStore;
    private readonly TemplateRenderer _renderer;
    private readonly WhatsappCloudClient _whatsappClient;
    private readonly LimiteDiarioWhatsapp _limiteWhatsapp;

    public NotificadorResolver(
        ConfigStore configStore,
        TemplateRenderer renderer,
        WhatsappCloudClient whatsappClient,
        LimiteDiarioWhatsapp limiteWhatsapp)
    {
        _configStore = configStore;
        _renderer = renderer;
        _whatsappClient = whatsappClient;
        _limiteWhatsapp = limiteWhatsapp;
    }

    public INotificador Resolver(EnumMeioNotificacao meio) => meio switch
    {
        EnumMeioNotificacao.EMail => ResolverEmail(),
        EnumMeioNotificacao.WhatsApp => ResolverWhatsapp(),
        _ => throw new NotSupportedException($"Meio de notificação não configurado neste worker: {meio}")
    };

    private INotificador ResolverEmail()
    {
        if (!_configStore.Canais.Email)
            throw new NotSupportedException("Meio de notificação não configurado neste worker: EMail");

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

    private INotificador ResolverWhatsapp()
    {
        if (!_configStore.Canais.Whatsapp)
            throw new NotSupportedException("Meio de notificação não configurado neste worker: WhatsApp");

        if (_configStore.Whatsapp is not { EstaCompleta: true })
            throw new NotSupportedException("Meio de notificação não configurado neste worker: WhatsApp");

        return new NotificadorWhatsapp(_whatsappClient, _limiteWhatsapp);
    }
}
