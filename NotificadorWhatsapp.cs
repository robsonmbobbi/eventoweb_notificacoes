using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EventoWebNotificacoes
{
    public class NotificadorWhatsapp : INotificador
    {
        private readonly TemplateRenderer _renderer;
        private readonly NotificationWorkerOptions.WhatsappOptions _config;

        public NotificadorWhatsapp(TemplateRenderer renderer, NotificationWorkerOptions.WhatsappOptions config)
        {
            _renderer = renderer;
            _config = config;
        }

        public async Task EnviarAsync(NotificacaoRequest notificacao)
        {
            // Render message body; let rendering exceptions propagate
            var texto = _renderer.Render(notificacao.TemplateConteudo, notificacao.DadosJson);

            // Validate destinatario minimally
            var dest = notificacao.Destinatario ?? string.Empty;
            var digits = Regex.Replace(dest, "\\D", "");
            if (digits.Length < 8)
                throw new Exception("Destinatário inválido para WhatsApp");

            using var clienteHttp = new HttpClient() { BaseAddress = new Uri(_config.ApiUrl) };
            clienteHttp.DefaultRequestHeaders.Accept.Clear();
            clienteHttp.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrWhiteSpace(_config.ApiKey))
                clienteHttp.DefaultRequestHeaders.Add("apikey", _config.ApiKey);

            var payload = new
            {
                number = notificacao.Destinatario,
                text = texto,
                instance = _config.Instance
            };

            var dadosEnviarJson = JsonSerializer.Serialize(payload);
            using var conteudoRequisicao = new StringContent(dadosEnviarJson, Encoding.UTF8, "application/json");

            using var response = await clienteHttp.PostAsync($"message/sendText/{_config.Instance}", conteudoRequisicao).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"WhatsApp API error: {response.StatusCode} - {errorContent}");
            }

        }
    }
}
