using EventoWeb.Comum.Negocio.Entidades.Notificacoes;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Text;

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

        public async Task EnviarAsync(MensagemNotificacao mensagem)
        {
            // Render subject and body. If rendering fails, let exception propagate so the caller records it.
            var assunto = mensagem.Modelo.Assunto ?? string.Empty;
            var subject = _renderer.Render(assunto, mensagem.VariaveisJson);
            var body = _renderer.Render(mensagem.Modelo.Mensagem, mensagem.VariaveisJson);

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
                        name = mensagem.Destinatario,
                        email = mensagem.Destinatario
                    }
                },
                subject,
                htmlContent = body
            };

            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var response = await clientHttp.PostAsync("v3/smtp/email", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Email API error: {response.StatusCode} - {errorContent}");
            }
        }
    }
}
