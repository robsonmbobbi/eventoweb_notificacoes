using LiteDB;

namespace EventoWebNotificacoes;

/// <summary>Credenciais da WhatsApp Cloud API (Meta). Ver README, seção "WhatsApp (Cloud API da Meta)".</summary>
public class WhatsappCloudOptions
{
    public string PhoneNumberId { get; set; } = string.Empty;
    public string WabaId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "v23.0";

    // Teto próprio de mensagens em 24h (janela móvel). Portfólios não verificados na Meta já são
    // limitados a 250 destinatários únicos/24h; este limite garante não passar disso.
    public int LimiteDiario { get; set; } = 250;

    // Token combinado com a Meta na configuração do webhook de status (GET /webhook/whatsapp).
    public string WebhookVerifyToken { get; set; } = string.Empty;

    [BsonIgnore]
    public bool EstaCompleta =>
        !string.IsNullOrWhiteSpace(PhoneNumberId) &&
        !string.IsNullOrWhiteSpace(WabaId) &&
        !string.IsNullOrWhiteSpace(AccessToken) &&
        !string.IsNullOrWhiteSpace(ApiVersion);
}
