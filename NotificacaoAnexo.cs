namespace EventoWebNotificacoes;

public sealed class NotificacaoAnexo
{
    public string NomeArquivo { get; set; } = string.Empty;
    public string? MimeType { get; set; }
    public byte[] Conteudo { get; set; } = Array.Empty<byte>();
}
