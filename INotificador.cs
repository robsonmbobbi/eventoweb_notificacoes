namespace EventoWebNotificacoes;

public interface INotificador
{
    Task EnviarAsync(NotificacaoRequest notificacao);
}
