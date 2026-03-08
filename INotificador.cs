using System.Threading.Tasks;
using EventoWeb.Comum.Negocio.Entidades.Notificacoes;

namespace EventoWebNotificacoes
{
    public interface INotificador
    {
        Task EnviarAsync(MensagemNotificacao mensagem);
    }
}
