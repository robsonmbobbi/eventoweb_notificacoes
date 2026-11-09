using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.StaticFiles;

namespace EventoWebNotificacoes
{
    /// <summary>
    /// Envia notificações pela WhatsApp Cloud API. Mensagens iniciadas pela empresa só podem ser
    /// templates aprovados pela Meta: o texto vive no template (<see cref="NotificacaoRequest.TemplateWhatsapp"/>)
    /// e as variáveis vêm das chaves de primeiro nível de <see cref="NotificacaoRequest.DadosJson"/>.
    /// </summary>
    public class NotificadorWhatsapp : INotificador
    {
        private const string IdiomaPadrao = "pt_BR";

        private static readonly Regex VariavelTemplate = new(@"\{\{\s*(\w+)\s*\}\}", RegexOptions.Compiled);
        private static readonly Regex QuebrasETabs = new(@"[\r\n\t]+", RegexOptions.Compiled);
        private static readonly Regex EspacosRepetidos = new(@" {4,}", RegexOptions.Compiled);
        private static readonly FileExtensionContentTypeProvider TiposMime = new();

        private readonly WhatsappCloudClient _client;

        public NotificadorWhatsapp(WhatsappCloudClient client)
        {
            _client = client;
        }

        public async Task EnviarAsync(NotificacaoRequest notificacao)
        {
            var dest = Regex.Replace(notificacao.Destinatario ?? string.Empty, "\\D", "");
            if (dest.Length < 8)
                throw new Exception("Destinatário inválido para WhatsApp");

            if (string.IsNullOrWhiteSpace(notificacao.TemplateWhatsapp))
                throw new Exception("TemplateWhatsapp é obrigatório: a API oficial só envia notificações por template aprovado pela Meta.");

            if (notificacao.Anexos.Count > 1)
                throw new Exception("WhatsApp aceita no máximo 1 anexo por notificação (cabeçalho do template).");

            var idioma = string.IsNullOrWhiteSpace(notificacao.IdiomaTemplateWhatsapp) ? IdiomaPadrao : notificacao.IdiomaTemplateWhatsapp;
            var template = await _client.ObterTemplateAsync(notificacao.TemplateWhatsapp, idioma).ConfigureAwait(false);
            var dados = LerDados(notificacao.DadosJson);

            // Tudo que pode falhar localmente é validado antes de subir mídia.
            var parametrosCabecalho = template.FormatoCabecalho == "TEXT"
                ? MontarParametrosTexto(template, template.TextoCabecalho, dados)
                : new List<object>();
            var parametrosCorpo = MontarParametrosTexto(template, template.TextoCorpo, dados);

            var anexo = notificacao.Anexos.FirstOrDefault();
            if (template.CabecalhoEhMidia && anexo is null)
                throw new Exception($"Template '{template.Nome}' tem cabeçalho de {template.FormatoCabecalho} e exige 1 anexo.");
            if (!template.CabecalhoEhMidia && anexo is not null)
                throw new Exception($"Template '{template.Nome}' não tem cabeçalho de mídia; não é possível enviar anexo com ele.");

            if (anexo is not null)
                parametrosCabecalho.Add(await MontarParametroMidiaAsync(template, anexo).ConfigureAwait(false));

            var componentes = new List<object>();
            if (parametrosCabecalho.Count > 0)
                componentes.Add(new { type = "header", parameters = parametrosCabecalho });
            if (parametrosCorpo.Count > 0)
                componentes.Add(new { type = "body", parameters = parametrosCorpo });

            await _client.EnviarTemplateAsync(dest, template.Nome, template.Idioma, componentes).ConfigureAwait(false);
        }

        private static List<object> MontarParametrosTexto(WhatsappTemplate template, string? texto, IReadOnlyDictionary<string, string> dados)
        {
            if (string.IsNullOrEmpty(texto))
                return new List<object>();

            var variaveis = VariavelTemplate.Matches(texto).Select(m => m.Groups[1].Value).Distinct().ToList();
            if (!template.ParametrosNomeados)
                variaveis = variaveis.OrderBy(v => int.TryParse(v, out var n) ? n : int.MaxValue).ToList();

            var faltando = variaveis.Where(v => !dados.ContainsKey(v)).ToList();
            if (faltando.Count > 0)
                throw new Exception($"DadosJson não tem as variáveis exigidas pelo template '{template.Nome}': {string.Join(", ", faltando)}.");

            return variaveis
                .Select(v => template.ParametrosNomeados
                    ? (object)new { type = "text", parameter_name = v, text = dados[v] }
                    : new { type = "text", text = dados[v] })
                .ToList();
        }

        private async Task<object> MontarParametroMidiaAsync(WhatsappTemplate template, NotificacaoAnexo anexo)
        {
            var mimeType = anexo.MimeType
                ?? (TiposMime.TryGetContentType(anexo.NomeArquivo, out var inferido) ? inferido : "application/octet-stream");

            var mediaId = await _client.UploadMidiaAsync(anexo.Conteudo, anexo.NomeArquivo, mimeType).ConfigureAwait(false);

            return template.FormatoCabecalho switch
            {
                "IMAGE" => new { type = "image", image = new { id = mediaId } },
                "VIDEO" => new { type = "video", video = new { id = mediaId } },
                _ => (object)new { type = "document", document = new { id = mediaId, filename = anexo.NomeArquivo } }
            };
        }

        // Só chaves de primeiro nível com valor escalar viram parâmetro. A Meta rejeita parâmetros
        // com quebra de linha, tab ou 4+ espaços seguidos (erro 132018), então eles são normalizados.
        private static IReadOnlyDictionary<string, string> LerDados(string? dadosJson)
        {
            var dados = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(dadosJson))
                return dados;

            using var doc = JsonDocument.Parse(dadosJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return dados;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var valor = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => prop.Value.GetRawText(),
                    _ => null
                };

                if (valor is not null)
                    dados[prop.Name] = EspacosRepetidos.Replace(QuebrasETabs.Replace(valor, " "), "   ").Trim();
            }

            return dados;
        }
    }
}
