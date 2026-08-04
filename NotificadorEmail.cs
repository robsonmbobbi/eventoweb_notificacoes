using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EventoWebNotificacoes
{
    public class NotificadorEmail : INotificador
    {
        private readonly TemplateRenderer _renderer;
        private readonly NotificationWorkerOptions.EmailOptions _config;

        public NotificadorEmail(TemplateRenderer renderer, NotificationWorkerOptions.EmailOptions config)
        {
            _renderer = renderer;
            _config = config;
        }

        public async Task EnviarAsync(NotificacaoRequest notificacao)
        {
            // Render subject and body. If rendering fails, let exception propagate so the caller records it.
            var assunto = notificacao.TemplateAssunto ?? string.Empty;
            var subject = _renderer.Render(assunto, notificacao.DadosJson);
            var body = _renderer.Render(notificacao.TemplateConteudo, notificacao.DadosJson);

            using var clientHttp = new HttpClient() { BaseAddress = new Uri(_config.ApiUrl) };
            clientHttp.DefaultRequestHeaders.Accept.Clear();
            clientHttp.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            clientHttp.DefaultRequestHeaders.Add("api-key", _config.ApiKey);

            var payload = new
            {
                sender = new
                {
                    name = _config.FromAddress,
                    email = _config.FromAddress
                },
                to = new[]
                {
                    new
                    {
                        name = notificacao.Destinatario,
                        email = notificacao.Destinatario
                    }
                },
                subject,
                htmlContent = body
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await clientHttp.PostAsync("v3/smtp/email", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Email API error: {response.StatusCode} - {errorContent}");
            }
        }
    }
}
