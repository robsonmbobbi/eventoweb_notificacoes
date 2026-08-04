namespace EventoWebNotificacoes;

public sealed record WhatsappConnectionState(
    EnumWhatsappStatus Status,
    string? QrCode,
    string? UltimoErro,
    DateTimeOffset UltimaAtualizacao);
