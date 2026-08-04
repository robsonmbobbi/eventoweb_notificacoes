using EventoWebNotificacoes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

builder.Services.Configure<NotificationWorkerOptions>(builder.Configuration.GetSection("Notification"));
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));

// Application services
builder.Services.AddSingleton<TemplateRenderer>();

builder.Services.AddSingleton<INotificadorResolver>(provider =>
{
    var conf = provider.GetRequiredService<IOptions<NotificationWorkerOptions>>().Value;
    var renderer = provider.GetRequiredService<TemplateRenderer>();
    var notificadores = new Dictionary<EnumMeioNotificacao, INotificador>();

    if (!string.IsNullOrWhiteSpace(conf.Email?.ApiUrl) &&
        !string.IsNullOrWhiteSpace(conf.Email?.ApiKey) &&
        !string.IsNullOrWhiteSpace(conf.Email?.FromAddress))
    {
        notificadores[EnumMeioNotificacao.EMail] = new NotificadorEmail(renderer, conf.Email);
    }

    if (!string.IsNullOrWhiteSpace(conf.Whatsapp?.ApiUrl) &&
        !string.IsNullOrWhiteSpace(conf.Whatsapp?.ApiKey) &&
        !string.IsNullOrWhiteSpace(conf.Whatsapp?.Instance))
    {
        notificadores[EnumMeioNotificacao.WhatsApp] = new NotificadorWhatsapp(renderer, conf.Whatsapp);
    }

    if (notificadores.Count == 0)
        throw new InvalidOperationException("Nenhum meio de notificação configurado (Notification:Email / Notification:Whatsapp).");

    return new NotificadorResolver(notificadores);
});

builder.Services.AddHostedService<RabbitMqRpcServerService>();

var app = builder.Build();

app.Run();
