using Scriban;
using Scriban.Runtime;
using System.Dynamic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EventoWebNotificacoes
{
    public class TemplateRenderer
    {
        public string Render(string templateText, string? variaveisJson)
        {
            var template = Template.Parse(templateText ?? string.Empty);
            if (template.HasErrors)
            {
                var msgs = string.Join("; ", template.Messages.Select(m => m.ToString()));
                throw new Exception("Template parse error: " + msgs);
            }

            var scriptObject = new ScriptObject();

            if (!string.IsNullOrWhiteSpace(variaveisJson))
            {
                using var doc = JsonDocument.Parse(variaveisJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        scriptObject[prop.Name] = ConvertJsonElement(prop.Value);
                    }
                }
            }

            var context = new TemplateContext();
            context.PushGlobal(scriptObject);
            
            // Render may throw; let exception propagate to caller
            return template.Render(context);
        }

        private object? ConvertJsonElement(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out var l) ? (object)l : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Object => ConvertObject(element),
                JsonValueKind.Array => ConvertArray(element),
                _ => null,
            };
        }

        private Dictionary<string, object?> ConvertObject(JsonElement element)
        {
            var dict = new Dictionary<string, object?>();
            foreach (var prop in element.EnumerateObject())
            {
                dict[prop.Name] = ConvertJsonElement(prop.Value);
            }
            return dict;
        }

        private object[] ConvertArray(JsonElement element)
        {
            var list = new List<object>();
            foreach (var item in element.EnumerateArray())
            {
                list.Add(ConvertJsonElement(item));
            }
            return list.ToArray();
        }
    }

    public class TemplateFunctionsScriban : ScriptObject
    {
        public static string FormatPhone(string phone)
        {
            return Convert.ToUInt64(phone).ToString(@"(00) 0 0000-0000");
        }
    }
}
