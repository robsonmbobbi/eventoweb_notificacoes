using EventoWebNotificacoes;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));

// Application services
builder.Services.AddSingleton<TemplateRenderer>();
builder.Services.AddSingleton<ConfigStore>();
builder.Services.AddSingleton<ReadinessGate>();

// WhatsApp Cloud API (Meta). O cliente é singleton para compartilhar o cache de templates.
builder.Services.AddHttpClient(nameof(WhatsappCloudClient), http => http.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddSingleton<WhatsappCloudClient>();
builder.Services.AddSingleton<LimiteDiarioWhatsapp>();

// Sempre hosted service: revalida as credenciais no boot e a cada mudança de Canais/WhatsApp
// pela UI, mantendo o ReadinessGate atualizado sem intervenção manual.
builder.Services.AddSingleton<WhatsappValidacaoService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<WhatsappValidacaoService>());

builder.Services.AddSingleton<INotificadorResolver, NotificadorResolver>();

builder.Services.AddSingleton<RabbitMqRpcServerService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<RabbitMqRpcServerService>());

var app = builder.Build();

app.UseStaticFiles();
app.MapGet("/", () => Results.Redirect("/admin.html"));
app.MapConfiguracaoEndpoints();
app.MapWhatsappWebhookEndpoints();

app.Run();
