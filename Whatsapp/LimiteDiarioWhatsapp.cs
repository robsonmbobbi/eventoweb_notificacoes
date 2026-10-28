using LiteDB;

namespace EventoWebNotificacoes;

/// <summary>
/// Teto de mensagens de WhatsApp em uma janela móvel de 24h, persistido no LiteDB para
/// sobreviver a restarts. Cada envio reserva uma vaga antes de chamar a Meta e a devolve se falhar.
/// </summary>
public class LimiteDiarioWhatsapp
{
    private static readonly TimeSpan Janela = TimeSpan.FromHours(24);

    private readonly ConfigStore _configStore;
    private readonly ILiteCollection<EnvioWhatsapp> _envios;
    private readonly object _lock = new();

    public LimiteDiarioWhatsapp(ConfigStore configStore)
    {
        _configStore = configStore;
        _envios = configStore.Database.GetCollection<EnvioWhatsapp>("whatsapp_envios");
        _envios.EnsureIndex(e => e.EnviadoEm);
    }

    public int Limite => _configStore.Whatsapp?.LimiteDiario ?? new WhatsappCloudOptions().LimiteDiario;

    public int ContarUltimas24h()
    {
        lock (_lock)
        {
            var corte = DateTime.UtcNow - Janela;
            return _envios.Count(e => e.EnviadoEm > corte);
        }
    }

    /// <summary>Reserva uma vaga; lança se o limite das últimas 24h já foi atingido.</summary>
    public ObjectId Reservar()
    {
        lock (_lock)
        {
            var agora = DateTime.UtcNow;
            var corte = agora - Janela;
            _envios.DeleteMany(e => e.EnviadoEm <= corte);

            var limite = Limite;
            if (_envios.Count() >= limite)
            {
                var maisAntigo = _envios.Query().OrderBy(e => e.EnviadoEm).FirstOrDefault()?.EnviadoEm ?? agora;
                var liberaEm = (maisAntigo.ToUniversalTime() + Janela).ToLocalTime();
                throw new InvalidOperationException(
                    $"Limite diário de WhatsApp atingido ({limite} mensagens nas últimas 24h); " +
                    $"próxima vaga após {liberaEm:dd/MM HH:mm}.");
            }

            var envio = new EnvioWhatsapp { Id = ObjectId.NewObjectId(), EnviadoEm = agora };
            _envios.Insert(envio);
            return envio.Id;
        }
    }

    public void Liberar(ObjectId reserva)
    {
        lock (_lock)
            _envios.Delete(reserva);
    }

    public sealed class EnvioWhatsapp
    {
        public ObjectId Id { get; set; } = ObjectId.Empty;
        public DateTime EnviadoEm { get; set; }
    }
}
