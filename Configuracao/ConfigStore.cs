using LiteDB;
using Microsoft.Extensions.Options;

namespace EventoWebNotificacoes;

public class ConfigStore : IDisposable
{
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<RabbitMqOptions> _rabbitMqCollection;
    private readonly ILiteCollection<NotificationWorkerOptions.EmailOptions> _emailCollection;
    private readonly ILiteCollection<CanaisOptions> _canaisCollection;

    private readonly object _lock = new();
    private RabbitMqOptions _rabbitMq;
    private NotificationWorkerOptions.EmailOptions? _email;
    private CanaisOptions _canais;

    public ConfigStore(IOptions<StorageOptions> storageOptions, IConfiguration configuration)
    {
        var path = storageOptions.Value.ConfigDbPath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _db = new LiteDatabase(path);
        _rabbitMqCollection = _db.GetCollection<RabbitMqOptions>("rabbitmq");
        _emailCollection = _db.GetCollection<NotificationWorkerOptions.EmailOptions>("email");
        _canaisCollection = _db.GetCollection<CanaisOptions>("canais");

        _rabbitMq = _rabbitMqCollection.FindAll().FirstOrDefault() ?? SeedRabbitMq(configuration);
        _email = _emailCollection.FindAll().FirstOrDefault() ?? SeedEmail(configuration);
        _canais = _canaisCollection.FindAll().FirstOrDefault() ?? SeedCanais(configuration);
    }

    public RabbitMqOptions RabbitMq
    {
        get { lock (_lock) return _rabbitMq; }
    }

    public NotificationWorkerOptions.EmailOptions? Email
    {
        get { lock (_lock) return _email; }
    }

    public CanaisOptions Canais
    {
        get { lock (_lock) return _canais; }
    }

    public event Action? ConfigChanged;
    public event Action<CanaisOptions>? CanaisChanged;

    public void SalvarRabbitMq(RabbitMqOptions options)
    {
        lock (_lock)
        {
            _rabbitMqCollection.DeleteAll();
            _rabbitMqCollection.Insert(options);
            _rabbitMq = options;
        }
        ConfigChanged?.Invoke();
    }

    public void SalvarEmail(NotificationWorkerOptions.EmailOptions options)
    {
        lock (_lock)
        {
            _emailCollection.DeleteAll();
            _emailCollection.Insert(options);
            _email = options;
        }
        ConfigChanged?.Invoke();
    }

    public void SalvarCanais(CanaisOptions options)
    {
        lock (_lock)
        {
            _canaisCollection.DeleteAll();
            _canaisCollection.Insert(options);
            _canais = options;
        }
        ConfigChanged?.Invoke();
        CanaisChanged?.Invoke(options);
    }

    private RabbitMqOptions SeedRabbitMq(IConfiguration configuration)
    {
        var seed = new RabbitMqOptions();
        configuration.GetSection("RabbitMq").Bind(seed);
        _rabbitMqCollection.Insert(seed);
        return seed;
    }

    private NotificationWorkerOptions.EmailOptions? SeedEmail(IConfiguration configuration)
    {
        var section = configuration.GetSection("Notification:Email");
        if (!section.Exists())
            return null;

        var seed = new NotificationWorkerOptions.EmailOptions();
        section.Bind(seed);
        if (string.IsNullOrWhiteSpace(seed.ApiUrl) && string.IsNullOrWhiteSpace(seed.ApiKey) && string.IsNullOrWhiteSpace(seed.FromAddress))
            return null;

        _emailCollection.Insert(seed);
        return seed;
    }

    private CanaisOptions SeedCanais(IConfiguration configuration)
    {
        var seed = new CanaisOptions();
        configuration.GetSection("Canais").Bind(seed);

        // Só bloqueia no seed inicial (banco vazio); depois do primeiro boot, quem impede os
        // dois canais desligados é o PUT /api/config/canais (validação em runtime).
        if (!seed.Email && !seed.Whatsapp)
        {
            throw new InvalidOperationException(
                "Configuração inicial inválida: nenhum canal habilitado em Canais:Email/Canais:Whatsapp " +
                "no appsettings.json usado para o seed inicial. Habilite pelo menos um.");
        }

        _canaisCollection.Insert(seed);
        return seed;
    }

    public void Dispose() => _db.Dispose();
}
