using Microsoft.EntityFrameworkCore;

namespace BizAssistant.Api.Data;

public static class DbContextExtensions
{
    /// <summary>
    /// The original business (lowest id): the one the config-based admin owns, and the default target for the
    /// n8n website form. Returns 0 if there are no businesses yet. Ignores the tenant filter on purpose.
    /// </summary>
    public static async Task<int> DefaultBusinessIdAsync(this AppDbContext db, CancellationToken ct = default) =>
        await db.BusinessProfiles.IgnoreQueryFilters().AsNoTracking()
            .OrderBy(p => p.Id)
            .Select(p => p.Id)
            .FirstOrDefaultAsync(ct);
}
