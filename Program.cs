using EventoWebNotificacoes;

var builder = WebApplication.CreateBuilder(args);

// Toda a configuração vem do appsettings.json/variáveis de ambiente e é lida no boot;
// mudanças exigem restart. Canal habilitado com configuração incompleta derruba o start.
var canais = builder.Configuration.GetSection("Canais").Get<CanaisOptions>() ?? new CanaisOptions();
if (!canais.Email && !canais.Whatsapp)
{
    throw new InvalidOperationException(
        "Configuração inválida: nenhum canal habilitado em Canais:Email/Canais:Whatsapp " +
        "no appsettings.json. Habilite pelo menos um.");
}

builder.Services.AddOptions<CanaisOptions>().Bind(builder.Configuration.GetSection("Canais"));
builder.Services.AddOptions<RabbitMqOptions>().Bind(builder.Configuration.GetSection("RabbitMq"));

builder.Services.AddOptions<NotificationWorkerOptions.EmailOptions>()
    .Bind(builder.Configuration.GetSection("Notification:Email"))
    .Validate(email => !canais.Email || email.EstaCompleta,
        "Canal Email habilitado, mas Notification:Email (ApiUrl, ApiKey, FromAddress) está incompleto no appsettings.json.")
    .ValidateOnStart();

builder.Services.AddOptions<WhatsappCloudOptions>()
    .Bind(builder.Configuration.GetSection("Whatsapp"))
    .Validate(whatsapp => !canais.Whatsapp || whatsapp.EstaCompleta,
        "Canal WhatsApp habilitado, mas Whatsapp (PhoneNumberId, WabaId, AccessToken, ApiVersion) está incompleto no appsettings.json.")
    .ValidateOnStart();

// Application services
builder.Services.AddSingleton<TemplateRenderer>();
builder.Services.AddSingleton<ReadinessGate>();

// WhatsApp Cloud API (Meta). O cliente é singleton para compartilhar o cache de templates.
builder.Services.AddHttpClient(nameof(WhatsappCloudClient), http => http.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddSingleton<WhatsappCloudClient>();

// Valida as credenciais do WhatsApp no boot (com retry) e libera o ReadinessGate.
builder.Services.AddHostedService<WhatsappValidacaoService>();

builder.Services.AddSingleton<INotificadorResolver, NotificadorResolver>();

builder.Services.AddHostedService<RabbitMqRpcServerService>();

var app = builder.Build();

// Loga o status de cada canal antes do ValidateOnStart (que derruba o start se faltar configuração).
StatusCanais.LogarResumo(
    app.Logger,
    canais,
    builder.Configuration.GetSection("Notification:Email").Get<NotificationWorkerOptions.EmailOptions>() ?? new(),
    builder.Configuration.GetSection("Whatsapp").Get<WhatsappCloudOptions>() ?? new());

app.MapWhatsappWebhookEndpoints();

app.Run();
