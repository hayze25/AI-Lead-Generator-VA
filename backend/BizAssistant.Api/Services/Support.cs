using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BizAssistant.Api.Services;

/// <summary>An external dependency (LLM API, n8n) failed. The message is safe to show to the user.</summary>
public class IntegrationException(string message, Exception? inner = null) : Exception(message, inner);

public static class TextExtensions
{
    public static string Truncate(this string value, int max) =>
        value.Length <= max ? value : value[..max];

    public static string FirstName(this string fullName)
    {
        var first = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrEmpty(first) ? "there" : first;
    }
}

public class LlmOptions
{
    public string ApiKey { get; set; } = "";
    /// <summary>"Anthropic" (Messages API) or "OpenAI" (any OpenAI-compatible chat API, e.g. OpenRouter).</summary>
    public string Provider { get; set; } = "Anthropic";
    public string Model { get; set; } = "claude-sonnet-5-5";
    public string BaseUrl { get; set; } = "https://api.anthropic.com";
    public int MaxTokens { get; set; } = 1024;
}

public class N8nOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5678";
    /// <summary>When true, emails are logged instead of sent to n8n. Handy for local dev without n8n.</summary>
    public bool DryRun { get; set; }
}

/// <summary>
/// Protects the endpoints that n8n calls. n8n must send the shared secret in the X-Api-Key header.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ApiKeyAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var expected = context.HttpContext.RequestServices
            .GetRequiredService<IConfiguration>()["Automation:ApiKey"] ?? "";
        var provided = context.HttpContext.Request.Headers["X-Api-Key"].ToString();

        var valid = expected.Length > 0 && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));

        if (!valid)
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Invalid or missing API key." });
            return;
        }

        await next();
    }
}
