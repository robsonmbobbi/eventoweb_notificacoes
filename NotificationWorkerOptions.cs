namespace EventoWebNotificacoes;

public class NotificationWorkerOptions
{
    public EmailOptions? Email { get; set; }

    public class EmailOptions
    {
        public string ApiUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string FromAddress { get; set; } = string.Empty;

        public IReadOnlyList<string> CamposFaltantes =>
            new (string Nome, string Valor)[]
            {
                (nameof(ApiUrl), ApiUrl),
                (nameof(ApiKey), ApiKey),
                (nameof(FromAddress), FromAddress)
            }
            .Where(c => string.IsNullOrWhiteSpace(c.Valor))
            .Select(c => c.Nome)
            .ToList();

        public bool EstaCompleta => CamposFaltantes.Count == 0;
    }
}
