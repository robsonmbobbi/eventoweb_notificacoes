using System.Text.Json;
using Microsoft.Extensions.Options;

namespace EventoWebNotificacoes;

/// <summary>
/// Webhook da WhatsApp Cloud API. A Meta só informa o destino de uma mensagem aceita (enviada,
/// entregue, lida ou falha com o código do erro) por aqui; os status são apenas registrados em log.
/// </summary>
public static class WhatsappWebhookEndpoints
{
    public static void MapWhatsappWebhookEndpoints(this WebApplication app)
    {
        // Verificação feita pela Meta ao cadastrar a URL do webhook no app.
        app.MapGet("/webhook/whatsapp", (HttpRequest request, IOptions<WhatsappCloudOptions> options) =>
        {
            var token = options.Value.WebhookVerifyToken;
            var query = request.Query;

            if (query["hub.mode"] == "subscribe" &&
                !string.IsNullOrEmpty(token) &&
                query["hub.verify_token"] == token)
                return Results.Text(query["hub.challenge"].ToString());

            return Results.StatusCode(StatusCodes.Status403Forbidden);
        });

        // Sempre 200: qualquer outra resposta faz a Meta reenviar o mesmo evento por horas.
        app.MapPost("/webhook/whatsapp", async (HttpRequest request, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger(typeof(WhatsappWebhookEndpoints));
            try
            {
                using var doc = await JsonDocument.ParseAsync(request.Body);
                RegistrarStatus(doc.RootElement, logger);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Webhook WhatsApp: corpo não é JSON válido.");
            }
            return Results.Ok();
        });
    }

    private static void RegistrarStatus(JsonElement root, ILogger logger)
    {
        foreach (var entry in Itens(root, "entry"))
        foreach (var change in Itens(entry, "changes"))
        {
            if (!change.TryGetProperty("value", out var value))
                continue;

            foreach (var status in Itens(value, "statuses"))
            {
                var wamid = LerString(status, "id");
                var destinatario = LerString(status, "recipient_id");
                var situacao = LerString(status, "status");
                var categoria = status.TryGetProperty("pricing", out var pricing) ? LerString(pricing, "category") : null;

                if (situacao != "failed")
                {
                    logger.LogInformation("Webhook WhatsApp: wamid={Wamid} recipient_id={Destinatario} status={Status} categoria={Categoria}",
                        wamid, destinatario, situacao, categoria);
                    continue;
                }

                var erros = Itens(status, "errors").ToList();
                if (erros.Count == 0)
                    logger.LogWarning("Webhook WhatsApp: wamid={Wamid} recipient_id={Destinatario} falhou sem detalhes.", wamid, destinatario);

                foreach (var erro in erros)
                {
                    int? codigo = erro.TryGetProperty("code", out var c) && c.TryGetInt32(out var v) ? v : null;
                    var detalhes = erro.TryGetProperty("error_data", out var dados) ? LerString(dados, "details") : null;
                    logger.LogWarning(
                        "Webhook WhatsApp: wamid={Wamid} recipient_id={Destinatario} FALHOU code={Codigo} title={Titulo} message={Mensagem} details={Detalhes}",
                        wamid, destinatario, codigo, LerString(erro, "title"), LerString(erro, "message"), detalhes);
                }
            }
        }
    }

    private static IEnumerable<JsonElement> Itens(JsonElement elemento, string propriedade) =>
        elemento.ValueKind == JsonValueKind.Object &&
        elemento.TryGetProperty(propriedade, out var lista) &&
        lista.ValueKind == JsonValueKind.Array
            ? lista.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

    private static string? LerString(JsonElement elemento, string propriedade) =>
        elemento.ValueKind == JsonValueKind.Object &&
        elemento.TryGetProperty(propriedade, out var valor) &&
        valor.ValueKind == JsonValueKind.String
            ? valor.GetString()
            : null;
}
