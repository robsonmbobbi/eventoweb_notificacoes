namespace EventoWebNotificacoes;

public class NotificationWorkerOptions
{
    public EmailOptions? Email { get; set; }

    public class EmailOptions
    {
        public string ApiUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string FromAddress { get; set; } = string.Empty;
    }
}
