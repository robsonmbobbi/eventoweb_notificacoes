namespace EventoWebNotificacoes;

public class RabbitMqOptions
{
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string RequestQueueName { get; set; } = "notificacoes.enviar";
    public ushort PrefetchCount { get; set; } = 1;
}
