using QRCoder;
using RabbitMQ.Client;

namespace EventoWebNotificacoes;

public static class ConfiguracaoEndpoints
{
    public static void MapConfiguracaoEndpoints(this WebApplication app)
    {
        app.MapGet("/api/config/rabbitmq", (ConfigStore configStore) =>
        {
            var options = configStore.RabbitMq;
            return Results.Ok(new RabbitMqConfigDto(
                options.HostName, options.Port, options.UserName,
                !string.IsNullOrEmpty(options.Password),
                options.VirtualHost, options.RequestQueueName, options.PrefetchCount));
        });

        app.MapPut("/api/config/rabbitmq", (RabbitMqConfigInput input, ConfigStore configStore, ReadinessGate gate) =>
        {
            var atual = configStore.RabbitMq;
            var options = new RabbitMqOptions
            {
                HostName = input.HostName,
                Port = input.Port,
                UserName = input.UserName,
                Password = string.IsNullOrEmpty(input.Password) ? atual.Password : input.Password,
                VirtualHost = input.VirtualHost,
                RequestQueueName = input.RequestQueueName,
                PrefetchCount = input.PrefetchCount
            };
            configStore.SalvarRabbitMq(options);
            gate.AtualizarRabbitMq(false);
            return Results.Ok();
        });

        app.MapPost("/api/config/rabbitmq/validate", async (ConfigStore configStore, ReadinessGate gate) =>
        {
            var options = configStore.RabbitMq;
            try
            {
                var factory = new ConnectionFactory
                {
                    HostName = options.HostName,
                    Port = options.Port,
                    UserName = options.UserName,
                    Password = options.Password,
                    VirtualHost = options.VirtualHost
                };
                await using var connection = await factory.CreateConnectionAsync();
                await using var channel = await connection.CreateChannelAsync();
                gate.AtualizarRabbitMq(true);
                return Results.Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                gate.AtualizarRabbitMq(false);
                return Results.Ok(new { ok = false, erro = ex.Message });
            }
        });

        app.MapPost("/api/config/rabbitmq/reconnect", (RabbitMqRpcServerService rabbitMqService) =>
        {
            rabbitMqService.ForcarReconexao();
            return Results.Ok();
        });

        app.MapGet("/api/config/email", (ConfigStore configStore) =>
        {
            var email = configStore.Email;
            return Results.Ok(new EmailConfigDto(
                email?.ApiUrl ?? string.Empty,
                !string.IsNullOrEmpty(email?.ApiKey),
                email?.FromAddress ?? string.Empty));
        });

        app.MapPut("/api/config/email", (EmailConfigInput input, ConfigStore configStore) =>
        {
            var atual = configStore.Email;
            var options = new NotificationWorkerOptions.EmailOptions
            {
                ApiUrl = input.ApiUrl,
                ApiKey = string.IsNullOrEmpty(input.ApiKey) ? (atual?.ApiKey ?? string.Empty) : input.ApiKey,
                FromAddress = input.FromAddress
            };
            configStore.SalvarEmail(options);
            return Results.Ok();
        });

        app.MapPost("/api/config/email/validate", (ConfigStore configStore, ReadinessGate gate) =>
        {
            var email = configStore.Email;
            var ok = email is not null &&
                !string.IsNullOrWhiteSpace(email.ApiUrl) &&
                !string.IsNullOrWhiteSpace(email.ApiKey) &&
                !string.IsNullOrWhiteSpace(email.FromAddress);
            gate.AtualizarEmail(ok);
            return Results.Ok(new { ok });
        });

        app.MapGet("/api/whatsapp/status", (WhatsappConnectionService whatsapp) =>
            Results.Ok(BuildWhatsappStatusDto(whatsapp.CurrentState)));

        app.MapPost("/api/whatsapp/logout", async (WhatsappConnectionService whatsapp) =>
        {
            await whatsapp.LogoutAsync();
            return Results.Ok();
        });

        app.MapGet("/api/status", (ReadinessGate gate) => Results.Ok(gate.ObterStatus()));
    }

    private static WhatsappStatusDto BuildWhatsappStatusDto(WhatsappConnectionState state)
    {
        string? qrPngBase64 = null;
        if (state.QrCode != null)
        {
            var generator = new QRCodeGenerator();
            var qrData = generator.CreateQrCode(state.QrCode, QRCodeGenerator.ECCLevel.L);
            var png = new PngByteQRCode(qrData).GetGraphic(10);
            qrPngBase64 = Convert.ToBase64String(png);
        }

        return new WhatsappStatusDto(state.Status.ToString(), qrPngBase64, state.UltimoErro, state.UltimaAtualizacao);
    }
}

public record RabbitMqConfigDto(string HostName, int Port, string UserName, bool TemSenha, string VirtualHost, string RequestQueueName, ushort PrefetchCount);
public record RabbitMqConfigInput(string HostName, int Port, string UserName, string? Password, string VirtualHost, string RequestQueueName, ushort PrefetchCount);
public record EmailConfigDto(string ApiUrl, bool TemApiKey, string FromAddress);
public record EmailConfigInput(string ApiUrl, string? ApiKey, string FromAddress);
public record WhatsappStatusDto(string Status, string? QrCodePngBase64, string? UltimoErro, DateTimeOffset UltimaAtualizacao);
