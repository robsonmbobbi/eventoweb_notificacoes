namespace EventoWebNotificacoes;

/// <summary>Credenciais da WhatsApp Cloud API (Meta). Ver README, seção "WhatsApp (Cloud API da Meta)".</summary>
public class WhatsappCloudOptions
{
    public string PhoneNumberId { get; set; } = string.Empty;
    public string WabaId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "v23.0";

    // Token combinado com a Meta na configuração do webhook de status (GET /webhook/whatsapp).
    public string WebhookVerifyToken { get; set; } = string.Empty;

    public IReadOnlyList<string> CamposFaltantes =>
        new (string Nome, string Valor)[]
        {
            (nameof(PhoneNumberId), PhoneNumberId),
            (nameof(WabaId), WabaId),
            (nameof(AccessToken), AccessToken),
            (nameof(ApiVersion), ApiVersion)
        }
        .Where(c => string.IsNullOrWhiteSpace(c.Valor))
        .Select(c => c.Nome)
        .ToList();

    public bool EstaCompleta => CamposFaltantes.Count == 0;
}
