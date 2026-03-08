namespace EventoWebNotificacoes;

public class NotificationWorkerOptions
{
    public string Meio { get; set; } = "EMail";
    public int IntervalSeconds { get; set; } = 30;

    public EmailOptions? Email { get; set; }
    public WhatsappOptions? Whatsapp { get; set; }

    public class EmailOptions
    {
        public string ApiUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string FromAddress { get; set; } = string.Empty;
    }

    public class WhatsappOptions
    {
        public string ApiUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string Instance { get; set; } = string.Empty;
    }
}
