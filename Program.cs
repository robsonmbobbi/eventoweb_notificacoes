using EventoWebNotificacoes;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<WhatsappSessionOptions>(builder.Configuration.GetSection("Whatsapp"));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));

// Application services
builder.Services.AddSingleton<TemplateRenderer>();
builder.Services.AddSingleton<ConfigStore>();
builder.Services.AddSingleton<ReadinessGate>();

// Sempre hosted service: precisa rodar continuamente para poder ficar ocioso ("Desabilitado")
// ou conectado, reagindo em tempo real a Canais:Whatsapp mudar via PUT /api/config/canais —
// deixou de ser uma decisão fixada no boot (ver ConfigStore.Canais/CanaisChanged).
builder.Services.AddSingleton<WhatsappConnectionService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<WhatsappConnectionService>());

builder.Services.AddSingleton<INotificadorResolver, NotificadorResolver>();

builder.Services.AddSingleton<RabbitMqRpcServerService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<RabbitMqRpcServerService>());

var app = builder.Build();

app.UseStaticFiles();
app.MapGet("/", () => Results.Redirect("/admin.html"));
app.MapConfiguracaoEndpoints();

app.Run();
