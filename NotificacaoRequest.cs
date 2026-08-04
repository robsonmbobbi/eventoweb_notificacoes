namespace EventoWebNotificacoes;

public sealed class NotificacaoRequest
{
    public Guid Id { get; set; }
    public EnumMeioNotificacao Meio { get; set; }
    public string Destinatario { get; set; } = string.Empty;
    public string? TemplateAssunto { get; set; }
    public string TemplateConteudo { get; set; } = string.Empty;
    public string? DadosJson { get; set; }
}
