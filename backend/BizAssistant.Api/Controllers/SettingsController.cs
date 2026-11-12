using BizAssistant.Api.Data;
using BizAssistant.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BizAssistant.Api.Controllers;

/// <summary>The business profile the AI uses as context for every triage and draft.</summary>
[ApiController]
[Route("api/settings")]
public class SettingsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var profile = await db.BusinessProfiles.AsNoTracking().OrderBy(p => p.Id).FirstOrDefaultAsync(ct);
        return Ok(profile ?? new BusinessProfile());
    }

    [HttpPut]
    public async Task<IActionResult> Put(UpdateProfileRequest req, CancellationToken ct)
    {
        // Empty string removes the logo. Only small raster images are accepted (no SVG: it can carry scripts).
        var logo = req.LogoDataUrl?.Trim() ?? "";
        if (logo.Length > 0)
        {
            var allowed = new[] { "data:image/png;base64,", "data:image/jpeg;base64,", "data:image/webp;base64," };
            var prefix = allowed.FirstOrDefault(p => logo.StartsWith(p, StringComparison.Ordinal));
            if (prefix is null)
                return BadRequest(new { error = "The logo must be a PNG, JPEG or WebP image." });
            if (logo.Length > 400_000)
                return BadRequest(new { error = "The logo is too large. Use an image under about 300 KB." });

            var b64 = logo[prefix.Length..];
            if (!Convert.TryFromBase64String(b64, new byte[b64.Length], out _))
                return BadRequest(new { error = "The logo image data is not valid." });
        }

        // The query filter limits this to the signed-in user's own business.
        var profile = await db.BusinessProfiles.OrderBy(p => p.Id).FirstOrDefaultAsync(ct);
        if (profile is null) return NotFound(new { error = "Business not found." });

        profile.Name = req.Name.Trim();
        profile.Description = req.Description?.Trim() ?? "";
        profile.Tone = req.Tone?.Trim() ?? "";
        profile.Faq = req.Faq?.Trim() ?? "";
        profile.LogoDataUrl = logo.Length > 0 ? logo : null;

        await db.SaveChangesAsync(ct);
        return Ok(profile);
    }
}
