using BizAssistant.Api.Data;
using BizAssistant.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BizAssistant.Api.Controllers;

/// <summary>
/// Public on purpose, because the sign-in page needs a name and logo before anyone is signed in.
/// Signed out, it returns the original business's branding. Signed in, it returns the caller's own business.
/// </summary>
[ApiController]
[Route("api/branding")]
[AllowAnonymous]
public class BrandingController(AppDbContext db, ICurrentTenant tenant, IConfiguration config) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var all = db.BusinessProfiles.IgnoreQueryFilters().AsNoTracking();
        var scoped = tenant.BusinessId > 0
            ? all.Where(p => p.Id == tenant.BusinessId)
            : all.OrderBy(p => p.Id).Take(1);

        var row = await scoped.Select(p => new { p.Name, p.LogoDataUrl }).FirstOrDefaultAsync(ct);

        return Ok(new
        {
            name = row?.Name ?? "Business Assistant",
            logoDataUrl = row?.LogoDataUrl,
            allowRegistration = config.GetValue("Auth:AllowRegistration", true)
        });
    }
}
