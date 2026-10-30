namespace EventoWebNotificacoes;

/// <summary>
/// Loga no boot o status de cada canal: desabilitado, habilitado e configurado, ou habilitado
/// com configuração incompleta (que em seguida derruba o start via ValidateOnStart).
/// </summary>
public static class StatusCanais
{
    public static void LogarResumo(
        ILogger logger,
        CanaisOptions canais,
        NotificationWorkerOptions.EmailOptions email,
        WhatsappCloudOptions whatsapp)
    {
        var operacionais = new List<string>();
        var comFalha = new List<string>();

        if (!canais.Email)
            logger.LogInformation("Canal Email: desabilitado (Canais:Email=false).");
        else if (email.EstaCompleta)
        {
            logger.LogInformation("Canal Email: habilitado e operacional.");
            operacionais.Add("Email");
        }
        else
        {
            logger.LogError("Canal Email: habilitado mas NÃO operacional — faltam {Campos} no appsettings.json.",
                string.Join(", ", email.CamposFaltantes.Select(c => $"Notification:Email:{c}")));
            comFalha.Add("Email (configuração incompleta)");
        }

        if (!canais.Whatsapp)
            logger.LogInformation("Canal WhatsApp: desabilitado (Canais:Whatsapp=false).");
        else if (whatsapp.EstaCompleta)
        {
            logger.LogInformation("Canal WhatsApp: habilitado, configuração completa (aguardando validação na Graph API).");
            operacionais.Add("WhatsApp (pendente de validação)");
        }
        else
        {
            logger.LogError("Canal WhatsApp: habilitado mas NÃO operacional — faltam {Campos} no appsettings.json.",
                string.Join(", ", whatsapp.CamposFaltantes.Select(c => $"Whatsapp:{c}")));
            comFalha.Add("WhatsApp (configuração incompleta)");
        }

        var resumo = "Canais operacionais: [{Operacionais}]. Habilitados com falha: [{ComFalha}].";
        var nenhum = "nenhum";
        var args = new object[]
        {
            operacionais.Count > 0 ? string.Join(", ", operacionais) : nenhum,
            comFalha.Count > 0 ? string.Join(", ", comFalha) : nenhum
        };

        if (comFalha.Count > 0)
            logger.LogError(resumo, args);
        else
            logger.LogInformation(resumo, args);
    }
}
