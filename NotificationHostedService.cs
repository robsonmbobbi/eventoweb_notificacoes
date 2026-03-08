using EventoWeb.Comum.Negocio.Entidades.Notificacoes;
using EventoWeb.Comum.Negocio.Repositorios;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventoWebNotificacoes;

public class NotificationHostedService : BackgroundService
{
    private readonly ILogger<NotificationHostedService> _logger;
    private readonly IContexto _contexto;
    private readonly IMensagens _mensagens;
    private readonly INotificador _notificador;
    private readonly EnumMeioNotificacao _meioNotificacao;
    private readonly int _intervalSeconds;

    public NotificationHostedService(ILogger<NotificationHostedService> logger,
        IContexto contexto,
        IMensagens mensagens,
        INotificador notificador,
        EnumMeioNotificacao meioNotificacao,
        int intervalSeconds)
    {
        _logger = logger;
        _mensagens = mensagens;
        _contexto = contexto;
        _notificador = notificador;
        _intervalSeconds = intervalSeconds;
        _meioNotificacao = meioNotificacao;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation($"NotificationHostedService iniciado. Meio={_meioNotificacao} IntervalSeconds={_intervalSeconds}");

        var intervalo = TimeSpan.FromSeconds(Math.Max(1, _intervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {                
                var pendentes = _mensagens.ListarPendentesPorMeio(_meioNotificacao);

                foreach (var msg in pendentes.ToList())
                {

                    try
                    {
                        _contexto.IniciarTransacao();

                        try
                        {
                            await _notificador.EnviarAsync(msg);
                            msg.RegistrarEnvio();
                        }
                        catch (Exception ex)
                        {
                            msg.RegistrarErro(ex.Message);

                            _logger.LogError(ex, "Erro ao enviar notificação Id={Id}", msg.Id);
                        }

                        _mensagens.Atualizar(msg);
                        _contexto.SalvarTransacao();
                    }
                    catch (Exception ex)
                    {
                        _contexto.CancelarTransacao();
                        _logger.LogError(ex, "Erro na transação para notificação Id={Id}", msg.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no loop de notificações");
            }

            await Task.Delay(intervalo, stoppingToken);
        }
    }
}
