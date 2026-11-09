using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace EventoWebNotificacoes;

/// <summary>
/// Cliente HTTP da WhatsApp Cloud API (Graph API da Meta), com as credenciais da seção
/// <c>Whatsapp</c> do appsettings.
/// </summary>
public class WhatsappCloudClient
{
    private const string GraphBaseUrl = "https://graph.facebook.com";
    private static readonly TimeSpan TemplateCacheTtl = TimeSpan.FromMinutes(10);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WhatsappCloudOptions _options;
    private readonly ILogger<WhatsappCloudClient> _logger;

    // Evita uma consulta à Graph API por notificação só para descobrir as variáveis do template.
    private readonly ConcurrentDictionary<(string Nome, string Idioma), (WhatsappTemplate Template, DateTimeOffset ExpiraEm)> _templateCache = new();

    public WhatsappCloudClient(IHttpClientFactory httpClientFactory, IOptions<WhatsappCloudOptions> options, ILogger<WhatsappCloudClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<WhatsappNumeroInfo> ValidarAsync(CancellationToken ct = default)
    {
        var options = ObterOptions();
        using var doc = await EnviarRequisicaoAsync(options, HttpMethod.Get,
            $"{options.PhoneNumberId}?fields=display_phone_number,verified_name,quality_rating", conteudo: null, ct);

        var root = doc.RootElement;
        return new WhatsappNumeroInfo(
            LerString(root, "display_phone_number"),
            LerString(root, "verified_name"),
            LerString(root, "quality_rating"));
    }

    public async Task<WhatsappTemplate> ObterTemplateAsync(string nome, string idioma, CancellationToken ct = default)
    {
        var chave = (nome, idioma);
        if (_templateCache.TryGetValue(chave, out var cache) && cache.ExpiraEm > DateTimeOffset.UtcNow)
            return cache.Template;

        var options = ObterOptions();
        using var doc = await EnviarRequisicaoAsync(options, HttpMethod.Get,
            $"{options.WabaId}/message_templates?name={Uri.EscapeDataString(nome)}" +
            "&fields=name,language,status,category,parameter_format,components&limit=100",
            conteudo: null, ct);

        // O filtro "name" da Graph API não é exato; o mesmo nome também pode existir em vários idiomas.
        var encontrado = doc.RootElement.GetProperty("data").EnumerateArray()
            .FirstOrDefault(t => LerString(t, "name") == nome && LerString(t, "language") == idioma);

        if (encontrado.ValueKind != JsonValueKind.Object)
            throw new WhatsappApiException($"Template '{nome}' ({idioma}) não encontrado na conta do WhatsApp Business.");

        var template = WhatsappTemplate.FromJson(encontrado);
        if (!string.Equals(template.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
            throw new WhatsappApiException($"Template '{nome}' ({idioma}) não está aprovado pela Meta (status: {template.Status}).");

        _templateCache[chave] = (template, DateTimeOffset.UtcNow + TemplateCacheTtl);
        return template;
    }

    /// <summary>Sobe um arquivo para a Meta e devolve o media id, usado no cabeçalho do template.</summary>
    public async Task<string> UploadMidiaAsync(byte[] conteudo, string nomeArquivo, string mimeType, CancellationToken ct = default)
    {
        var options = ObterOptions();

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("whatsapp"), "messaging_product");
        form.Add(new StringContent(mimeType), "type");
        var arquivo = new ByteArrayContent(conteudo);
        arquivo.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        form.Add(arquivo, "file", nomeArquivo);

        using var doc = await EnviarRequisicaoAsync(options, HttpMethod.Post, $"{options.PhoneNumberId}/media", form, ct);
        return LerString(doc.RootElement, "id")
            ?? throw new WhatsappApiException("Upload de mídia não retornou um id.");
    }

    /// <summary>Envia uma mensagem de template e devolve o id da mensagem (wamid).</summary>
    public async Task<string> EnviarTemplateAsync(string destinatario, string nomeTemplate, string idioma, IReadOnlyList<object> componentes, CancellationToken ct = default)
    {
        var options = ObterOptions();

        var payload = new Dictionary<string, object>
        {
            ["messaging_product"] = "whatsapp",
            ["recipient_type"] = "individual",
            ["to"] = destinatario,
            ["type"] = "template",
            ["template"] = new Dictionary<string, object>
            {
                ["name"] = nomeTemplate,
                ["language"] = new { code = idioma },
                ["components"] = componentes
            }
        };

        var json = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var doc = await EnviarRequisicaoAsync(options, HttpMethod.Post, $"{options.PhoneNumberId}/messages", json, ct);

        var mensagem = PrimeiroItem(doc.RootElement, "messages");
        var contato = PrimeiroItem(doc.RootElement, "contacts");
        var wamid = LerString(mensagem, "id") ?? string.Empty;
        var input = LerString(contato, "input");
        var waId = LerString(contato, "wa_id");

        _logger.LogInformation("WhatsApp enviado: wamid={Wamid} input={Input} wa_id={WaId} status={Status}",
            wamid, input, waId, LerString(mensagem, "message_status"));

        // No Brasil o wa_id pode vir sem o nono dígito; se diferir do número enviado, é a primeira suspeita
        // quando a Meta aceita mas a mensagem não chega.
        if (waId is not null && input is not null && waId != input)
            _logger.LogWarning("WhatsApp: wa_id {WaId} difere do número enviado {Input} (wamid={Wamid}).", waId, input, wamid);

        return wamid;
    }

    private static JsonElement PrimeiroItem(JsonElement root, string propriedade) =>
        root.TryGetProperty(propriedade, out var lista) && lista.ValueKind == JsonValueKind.Array && lista.GetArrayLength() > 0
            ? lista[0]
            : default;

    private WhatsappCloudOptions ObterOptions()
    {
        if (!_options.EstaCompleta)
            throw new NotSupportedException("Meio de notificação não configurado neste worker: WhatsApp");
        return _options;
    }

    private async Task<JsonDocument> EnviarRequisicaoAsync(WhatsappCloudOptions options, HttpMethod metodo, string caminho, HttpContent? conteudo, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(metodo, $"{GraphBaseUrl}/{options.ApiVersion}/{caminho}") { Content = conteudo };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);

        var http = _httpClientFactory.CreateClient(nameof(WhatsappCloudClient));
        using var response = await http.SendAsync(request, ct);
        var corpo = await response.Content.ReadAsStringAsync(ct);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(corpo) ? "{}" : corpo);
        }
        catch (JsonException)
        {
            throw new WhatsappApiException($"Resposta inesperada da Graph API (HTTP {(int)response.StatusCode}): {corpo}");
        }

        if (!response.IsSuccessStatusCode || doc.RootElement.TryGetProperty("error", out _))
        {
            using (doc)
                throw ExtrairErro(doc.RootElement, response);
        }

        return doc;
    }

    private static WhatsappApiException ExtrairErro(JsonElement root, HttpResponseMessage response)
    {
        if (!root.TryGetProperty("error", out var erro))
            return new WhatsappApiException($"Graph API retornou HTTP {(int)response.StatusCode}.");

        int? codigo = erro.TryGetProperty("code", out var c) && c.TryGetInt32(out var v) ? v : null;
        string? detalhes = erro.TryGetProperty("error_data", out var dados) ? LerString(dados, "details") : null;
        return WhatsappApiException.DaGraph(codigo, LerString(erro, "message"), detalhes);
    }

    private static string? LerString(JsonElement elemento, string propriedade) =>
        elemento.ValueKind == JsonValueKind.Object &&
        elemento.TryGetProperty(propriedade, out var valor) &&
        valor.ValueKind == JsonValueKind.String
            ? valor.GetString()
            : null;
}

public sealed record WhatsappNumeroInfo(string? NumeroExibicao, string? NomeVerificado, string? Qualidade);

/// <summary>O que interessa de um template aprovado para montar a mensagem: formato e textos de cabeçalho/corpo.</summary>
public sealed record WhatsappTemplate(
    string Nome,
    string Idioma,
    string Status,
    bool ParametrosNomeados,
    string? FormatoCabecalho,
    string? TextoCabecalho,
    string? TextoCorpo)
{
    public bool CabecalhoEhMidia => FormatoCabecalho is "DOCUMENT" or "IMAGE" or "VIDEO";

    public static WhatsappTemplate FromJson(JsonElement t)
    {
        string? formatoCabecalho = null, textoCabecalho = null, textoCorpo = null;

        if (t.TryGetProperty("components", out var componentes))
        {
            foreach (var comp in componentes.EnumerateArray())
            {
                var tipo = comp.TryGetProperty("type", out var tp) ? tp.GetString() : null;
                var texto = comp.TryGetProperty("text", out var tx) ? tx.GetString() : null;

                if (string.Equals(tipo, "HEADER", StringComparison.OrdinalIgnoreCase))
                {
                    formatoCabecalho = comp.TryGetProperty("format", out var f) ? f.GetString()?.ToUpperInvariant() : null;
                    textoCabecalho = texto;
                }
                else if (string.Equals(tipo, "BODY", StringComparison.OrdinalIgnoreCase))
                {
                    textoCorpo = texto;
                }
            }
        }

        var formatoParametro = t.TryGetProperty("parameter_format", out var pf) ? pf.GetString() : null;

        return new WhatsappTemplate(
            t.GetProperty("name").GetString() ?? string.Empty,
            t.GetProperty("language").GetString() ?? string.Empty,
            t.TryGetProperty("status", out var s) ? s.GetString() ?? string.Empty : string.Empty,
            string.Equals(formatoParametro, "NAMED", StringComparison.OrdinalIgnoreCase),
            formatoCabecalho,
            textoCabecalho,
            textoCorpo);
    }
}
