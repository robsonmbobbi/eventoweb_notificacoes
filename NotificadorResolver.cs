using Microsoft.Extensions.Options;

namespace EventoWebNotificacoes;

public interface INotificadorResolver
{
    INotificador Resolver(EnumMeioNotificacao meio);
}

public sealed class NotificadorResolver : INotificadorResolver
{
    private readonly CanaisOptions _canais;
    private readonly NotificationWorkerOptions.EmailOptions _email;
    private readonly TemplateRenderer _renderer;
    private readonly WhatsappCloudClient _whatsappClient;

    public NotificadorResolver(
        IOptions<CanaisOptions> canais,
        IOptions<NotificationWorkerOptions.EmailOptions> email,
        TemplateRenderer renderer,
        WhatsappCloudClient whatsappClient)
    {
        _canais = canais.Value;
        _email = email.Value;
        _renderer = renderer;
        _whatsappClient = whatsappClient;
    }

    // A completude da configuração de cada canal habilitado já é garantida no start (ValidateOnStart).
    public INotificador Resolver(EnumMeioNotificacao meio) => meio switch
    {
        EnumMeioNotificacao.EMail when _canais.Email => new NotificadorEmail(_renderer, _email),
        EnumMeioNotificacao.WhatsApp when _canais.Whatsapp => new NotificadorWhatsapp(_whatsappClient),
        _ => throw new NotSupportedException($"Meio de notificação não configurado neste worker: {meio}")
    };
}
