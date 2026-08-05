using System.Text.RegularExpressions;

namespace EventoWebNotificacoes
{
    public class NotificadorWhatsapp : INotificador
    {
        private readonly TemplateRenderer _renderer;
        private readonly WhatsappConnectionService _connectionService;

        public NotificadorWhatsapp(TemplateRenderer renderer, WhatsappConnectionService connectionService)
        {
            _renderer = renderer;
            _connectionService = connectionService;
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

            await _connectionService.EnviarAsync(dest, texto).ConfigureAwait(false);
        }
    }
}
