using BizAssistant.Api.Models;
using Microsoft.Extensions.Options;

namespace BizAssistant.Api.Services;

public interface IAutomationNotifier
{
    /// <summary>Hands an email to the automation layer (n8n). Throws IntegrationException if it was not accepted.</summary>
    Task SendEmailAsync(EmailMessage message, CancellationToken ct = default);
}

/// <summary>
/// Posts to the n8n "Send email" webhook. The API only marks a reply as Sent
/// after n8n has accepted it, so the dashboard never claims an email went out when it did not.
/// </summary>
public class N8nNotifier(HttpClient http, IOptions<N8nOptions> options, ILogger<N8nNotifier> log) : IAutomationNotifier
{
    public async Task SendEmailAsync(EmailMessage message, CancellationToken ct = default)
    {
        var o = options.Value;

        if (o.DryRun)
        {
            log.LogInformation("[DryRun] Would send {Type} email #{RefId} to {To} - {Subject}",
                message.Type, message.RefId, message.To, message.Subject);
            return;
        }

        var url = $"{o.BaseUrl.TrimEnd('/')}/webhook/send-email";
        try
        {
            using var response = await http.PostAsJsonAsync(url, message, ct);
            if (!response.IsSuccessStatusCode)
            {
                log.LogError("n8n webhook returned {Status}", (int)response.StatusCode);
                throw new IntegrationException(
                    $"n8n did not accept the email (HTTP {(int)response.StatusCode}). Is the \"Send email\" workflow imported and active?");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogError(ex, "Could not reach n8n at {Url}", url);
            throw new IntegrationException("Could not reach n8n, so the email was not sent. Check that n8n is running.", ex);
        }
    }
}
