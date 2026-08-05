namespace EventoWebNotificacoes;

public class WhatsappNaoConectadoException : Exception
{
    public WhatsappNaoConectadoException()
        : base("Sessão do WhatsApp não está conectada.")
    {
    }
}
