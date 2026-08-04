using System.Text.Json;
using System.Text.RegularExpressions;
using BaileysCSharp.Core.Events;
using BaileysCSharp.Core.Helper;
using BaileysCSharp.Core.Models;
using BaileysCSharp.Core.Models.Sending.NonMedia;
using BaileysCSharp.Core.NoSQL;
using BaileysCSharp.Core.Sockets;
using BaileysCSharp.Core.Types;
using BaileysCSharp.Exceptions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QRCoder;

namespace EventoWebNotificacoes;

public class WhatsappConnectionService : BackgroundService
{
    private readonly ILogger<WhatsappConnectionService> _logger;
    private readonly WhatsappSessionOptions _options;
    private readonly ReadinessGate _gate;

    private static readonly TimeSpan MinRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    // BaileysCSharp fixa uma versão do protocolo WhatsApp Web em SocketConfig.Version que fica
    // desatualizada em poucos meses (o WhatsApp passa a rejeitar o handshake com "failure reason='405'").
    // Buscamos a versão vigente do arquivo mantido pelo projeto Baileys (JS) uma vez por processo.
    private const string BaileysVersionUrl = "https://raw.githubusercontent.com/WhiskeySockets/Baileys/master/src/Defaults/baileys-version.json";

    private readonly object _stateLock = new();
    private WhatsappConnectionState _state = new(EnumWhatsappStatus.Desconectado, null, null, DateTimeOffset.UtcNow);
    private WASocket? _socket;
    private uint[]? _version;
    private string? _credsFilePath;

    public WhatsappConnectionService(ILogger<WhatsappConnectionService> logger, IOptions<WhatsappSessionOptions> options, ReadinessGate gate)
    {
        _logger = logger;
        _options = options.Value;
        _gate = gate;
    }

    public WhatsappConnectionState CurrentState
    {
        get { lock (_stateLock) return _state; }
    }

    public event Action<WhatsappConnectionState>? StateChanged;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _version = await FetchLatestVersionAsync() ?? new SocketConfig().Version;
        _logger.LogInformation("Usando versão do protocolo WhatsApp Web {Version}", string.Join('.', _version));

        var retryDelay = MinRetryDelay;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
                retryDelay = MinRetryDelay;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Conexão com o WhatsApp perdida. Tentando novamente em {Delay}s.", retryDelay.TotalSeconds);
                SetState(EnumWhatsappStatus.Conectando, erro: ex.Message);

                try
                {
                    await Task.Delay(retryDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, MaxRetryDelay.TotalSeconds));
            }
        }

        SetState(EnumWhatsappStatus.Desconectado);
    }

    // Cada tentativa usa uma instância nova de WASocket: reaproveitar a mesma instância entre
    // reconexões deixa estado interno da lib inconsistente (o WebSocket antigo e o novo disputam
    // o mesmo campo, causando "Cannot send data if not connected" logo após o pareamento).
    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        var config = new SocketConfig { SessionName = _options.SessionName, Version = _version! };

        var credsFile = Path.Join(config.CacheRoot, "creds.json");
        _credsFilePath = credsFile;
        AuthenticationCreds? creds = File.Exists(credsFile)
            ? AuthenticationCreds.Deserialize(File.ReadAllText(credsFile))
            : null;
        creds ??= AuthenticationUtils.InitAuthCreds();

        BaseKeyStore keys = new FileKeyStore(config.CacheRoot);
        config.Auth = new AuthenticationState { Creds = creds, Keys = keys };

        var socket = new WASocket(config);
        _socket = socket;

        var connectionLost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loggedOut = false;

        socket.EV.Auth.Update += (_, e) => OnAuthUpdate(config, e);
        socket.EV.Connection.Update += (_, e) => OnConnectionUpdate(e, connectionLost, ref loggedOut);

        SetState(EnumWhatsappStatus.Conectando);
        socket.MakeSocket();

        using var cancellationRegistration = stoppingToken.Register(() => connectionLost.TrySetResult());
        await connectionLost.Task;

        socket.EndConnection(destroyStore: loggedOut);

        if (loggedOut)
        {
            // Sessão/credenciais inválidas: descarta o creds.json local para que a próxima
            // tentativa (já a seguir, no loop de reconexão) comece do zero e gere um QR novo.
            if (File.Exists(credsFile))
                File.Delete(credsFile);
            SetState(EnumWhatsappStatus.Desconectado, erro: "Sessão encerrada pelo WhatsApp. Gerando um novo QR Code...");
        }

        stoppingToken.ThrowIfCancellationRequested();
    }

    private async Task<uint[]?> FetchLatestVersionAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var json = await http.GetStringAsync(BaileysVersionUrl);
            using var doc = JsonDocument.Parse(json);
            var version = doc.RootElement.GetProperty("version").EnumerateArray().Select(e => e.GetUInt32()).ToArray();
            return version.Length == 3 ? version : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível obter a versão mais recente do protocolo WhatsApp Web; usando a versão padrão da lib (pode estar desatualizada).");
            return null;
        }
    }

    private void OnAuthUpdate(SocketConfig config, AuthenticationCreds creds)
    {
        var credsFile = Path.Join(config.CacheRoot, "creds.json");
        File.WriteAllText(credsFile, AuthenticationCreds.Serialize(creds));
    }

    private void OnConnectionUpdate(ConnectionState e, TaskCompletionSource connectionLost, ref bool loggedOut)
    {
        if (e.QR != null)
        {
            SetState(EnumWhatsappStatus.AguardandoQrCode, qr: e.QR);
            LogQrToConsole(e.QR);
        }

        if (e.Connection == WAConnectionState.Open)
        {
            SetState(EnumWhatsappStatus.Conectado);
        }

        if (e.Connection == WAConnectionState.Close)
        {
            if (e.LastDisconnect?.Error is Boom boom && boom.Data?.StatusCode == (int)DisconnectReason.LoggedOut)
                loggedOut = true;

            connectionLost.TrySetResult();
        }
    }

    private static void LogQrToConsole(string qr)
    {
        var generator = new QRCodeGenerator();
        var qrData = generator.CreateQrCode(qr, QRCodeGenerator.ECCLevel.L);
        var asciiQr = new AsciiQRCode(qrData);
        Console.WriteLine(asciiQr.GetGraphic(1));
    }

    private void SetState(EnumWhatsappStatus status, string? qr = null, string? erro = null)
    {
        WhatsappConnectionState newState;
        lock (_stateLock)
        {
            _state = newState = _state with
            {
                Status = status,
                QrCode = status == EnumWhatsappStatus.AguardandoQrCode ? qr : null,
                UltimoErro = erro,
                UltimaAtualizacao = DateTimeOffset.UtcNow
            };
        }
        _gate.AtualizarWhatsapp(status == EnumWhatsappStatus.Conectado);
        StateChanged?.Invoke(newState);
    }

    public async Task EnviarAsync(string destinatario, string texto, CancellationToken ct = default)
    {
        if (CurrentState.Status != EnumWhatsappStatus.Conectado || _socket == null)
            throw new WhatsappNaoConectadoException();

        var digits = Regex.Replace(destinatario, "\\D", "");
        var remoteJid = $"{digits}@s.whatsapp.net";

        await _socket.SendMessage(remoteJid, new TextMessageContent { Text = texto });
    }

    public Task LogoutAsync()
    {
        if (_credsFilePath != null && File.Exists(_credsFilePath))
            File.Delete(_credsFilePath);
        _socket?.EndConnection(destroyStore: true);
        return Task.CompletedTask;
    }
}
