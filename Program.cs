using EventoWeb.Comum.Negocio.Entidades.Notificacoes;
using EventoWeb.Comum.Negocio.Repositorios;
using EventoWeb.Comum.Persistencia.Mapeamentos;
using EventoWeb.Comum.Persistencia.Repositorios;
using EventoWebNotificacoes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NHibernate;
using NHibernate.Cfg;
using NHibernate.Mapping.ByCode;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

builder.Services.Configure<NotificationWorkerOptions>(builder.Configuration.GetSection("Notification"));

var databaseSection = builder.Configuration.GetSection("Database");
var connectionString = databaseSection.GetValue<string>("ConnectionString")
                        ?? throw new InvalidOperationException("Database:ConnectionString nao configurado.");
var nhDialect = databaseSection.GetValue<string>("Dialect")
               ?? "NHibernate.Dialect.MySQL5Dialect";
var nhDriver = databaseSection.GetValue<string>("Driver")
              ?? "NHibernate.Driver.MySqlDataDriver";

builder.Services.AddSingleton(_ =>
{
    NHibernate.Cfg.Environment.UseReflectionOptimizer = false;
    var configuration = new Configuration();
    configuration.SetProperty(NHibernate.Cfg.Environment.ConnectionProvider,
        "NHibernate.Connection.DriverConnectionProvider");
    configuration.SetProperty(NHibernate.Cfg.Environment.ConnectionString, connectionString);
    configuration.SetProperty(NHibernate.Cfg.Environment.Dialect, nhDialect);
    configuration.SetProperty(NHibernate.Cfg.Environment.ConnectionDriver, nhDriver);
    var mapper = new ModelMapper();
    var mappingAssembly = typeof(InscricaoMapping).Assembly;
    mapper.AddMappings(mappingAssembly.GetExportedTypes());
    configuration.AddMapping(mapper.CompileMappingForAllExplicitlyAddedEntities());

    return configuration.BuildSessionFactory();
});
builder.Services.AddScoped((provider) => {
    var factory = provider.GetService<ISessionFactory>() ??
                  throw new ArgumentNullException(nameof(ISessionFactory));

    return new ContextoNH(factory.OpenSession());
});

builder.Services.AddScoped<IContexto>(p => p.GetRequiredService<ContextoNH>());
builder.Services.AddScoped(p => p.GetRequiredService<ContextoNH>().MensagensNotificacao);

// Application services
builder.Services.AddSingleton<TemplateRenderer>();


// IMensagens is implemented in EventoWeb.Comum.Persistencia.Repositorios.MensagensNH
builder.Services.AddTransient(p => p.GetService<ContextoNH>()!.MensagensNotificacao);

builder.Services.AddHostedService(provider => {
    var conf = provider.GetRequiredService<IConfiguration>().GetSection("Notification").Get<NotificationWorkerOptions>()
               ?? throw new Exception("NotificationWorkerOptions configuration section is missing in appsettings.json");

    if (!Enum.TryParse<EnumMeioNotificacao>(conf.Meio, true, out var meioEnvio))
        throw new Exception($"Meio de notificação inválido: {conf.Meio}. Use 'EMail' ou 'WhatsApp'.");

    INotificador notificador;

    switch (meioEnvio)
    {
        case EnumMeioNotificacao.EMail:
            if (string.IsNullOrWhiteSpace(conf.Email?.ApiUrl) ||
                string.IsNullOrWhiteSpace(conf.Email?.ApiKey) ||
                string.IsNullOrWhiteSpace(conf.Email?.FromAddress))
            throw new Exception("Configuração de Email obrigatória para o meio selecionado.");

            notificador = new NotificadorEmail(provider.GetRequiredService<TemplateRenderer>(), conf.Email);
        break;
        case EnumMeioNotificacao.WhatsApp:

            if (string.IsNullOrWhiteSpace(conf.Whatsapp?.ApiUrl) || 
                string.IsNullOrWhiteSpace(conf.Whatsapp?.ApiKey) ||
                string.IsNullOrWhiteSpace(conf.Whatsapp?.Instance))
                throw new Exception("Configuração de Whatsapp obrigatória para o meio selecionado.");

            notificador = new NotificadorWhatsapp(provider.GetRequiredService<TemplateRenderer>(), conf.Whatsapp);
            break;
        default:
            throw new Exception("Meio de notificação não suportado.");
    }

    return new NotificationHostedService(
        provider.GetRequiredService<ILogger<NotificationHostedService>>(),
        provider.GetRequiredService<IContexto>(),
        provider.GetRequiredService<IMensagens>(),
        notificador,
        meioEnvio,
        conf.IntervalSeconds
    );
});

var app = builder.Build();

app.Run();
