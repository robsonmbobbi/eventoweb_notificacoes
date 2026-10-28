namespace EventoWebNotificacoes;

public class WhatsappApiException : Exception
{
    public int? Codigo { get; }

    public WhatsappApiException(string mensagem, int? codigo = null)
        : base(mensagem)
    {
        Codigo = codigo;
    }

    // Traduz os códigos de erro mais comuns da Graph API/Cloud API para algo acionável.
    // Lista completa: developers.facebook.com/docs/whatsapp/cloud-api/support/error-codes
    public static WhatsappApiException DaGraph(int? codigo, string? mensagem, string? detalhes)
    {
        var dica = codigo switch
        {
            190 => "Token de acesso inválido ou expirado — gere um token permanente de Usuário do Sistema.",
            100 => "Parâmetro inválido na chamada à API (confira Phone Number ID / WABA ID).",
            131047 => "Fora da janela de 24h: só é possível enviar mensagens de template.",
            131049 => "A Meta não entregou a mensagem para preservar o engajamento (política de marketing).",
            131056 or 130429 or 80007 => "Limite de taxa da Meta atingido; tente novamente mais tarde.",
            132000 => "Quantidade de parâmetros não confere com o template.",
            132001 => "Template não existe no idioma informado (ou ainda não foi aprovado).",
            132012 => "Formato de parâmetro não confere com o template.",
            133010 => "Número de telefone ainda não registrado na Cloud API.",
            _ => null
        };

        var partes = new[] { dica, mensagem, detalhes }.Where(p => !string.IsNullOrWhiteSpace(p));
        return new WhatsappApiException($"WhatsApp Cloud API (código {codigo?.ToString() ?? "?"}): {string.Join(" | ", partes)}", codigo);
    }
}
