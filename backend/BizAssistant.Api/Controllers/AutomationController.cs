using BizAssistant.Api.Data;
using BizAssistant.Api.Models;
using BizAssistant.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BizAssistant.Api.Controllers;

/// <summary>
/// Endpoints called by n8n. Every request must carry the X-Api-Key header.
/// n8n has no login, so these deliberately work across businesses (IgnoreQueryFilters) or act inside one
/// business that is named explicitly. The shared secret is what keeps strangers out.
/// </summary>
[ApiController]
[Route("api/automation")]
[AllowAnonymous] // exempt from the dashboard login: n8n authenticates with the X-Api-Key header instead
[ApiKey]
public class AutomationController(AppDbContext db, InquiryService svc, ICurrentTenant tenant) : ControllerBase
{
    private static readonly LeadStage[] ActiveStages = [LeadStage.New, LeadStage.Contacted, LeadStage.Qualified];

    /// <summary>Follow-ups that are due now and whose lead is still open, across all businesses.</summary>
    [HttpGet("followups/due")]
    public async Task<IActionResult> Due(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var rows = await db.FollowUps.IgnoreQueryFilters().AsNoTracking()
            .Where(f => f.Status == FollowUpStatus.Pending && f.DueAt <= now && ActiveStages.Contains(f.Lead.Stage))
            .OrderBy(f => f.DueAt)
            .Take(50)
            .Select(f => new { f.Id, f.LeadId, f.Lead.Name, f.Lead.Email, f.Message, f.Lead.BusinessId })
            .ToListAsync(ct);

        var businessIds = rows.Select(r => r.BusinessId).Distinct().ToList();
        var names = await db.BusinessProfiles.IgnoreQueryFilters().AsNoTracking()
            .Where(p => businessIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        return Ok(rows.Select(r => new DueFollowUpDto(
            r.Id, r.LeadId, r.Name, r.Email,
            $"Following up from {(names.TryGetValue(r.BusinessId, out var n) ? n : "us")}",
            r.Message)));
    }

    /// <summary>n8n calls this after it has sent the email. Safe to call twice.</summary>
    [HttpPost("followups/{id:int}/sent")]
    public async Task<IActionResult> MarkSent(int id, CancellationToken ct)
    {
        var followUp = await db.FollowUps.IgnoreQueryFilters().Include(f => f.Lead).FirstOrDefaultAsync(f => f.Id == id, ct);
        if (followUp is null) return NotFound(new { error = "Follow-up not found." });
        if (followUp.Status == FollowUpStatus.Sent) return Ok(new { followUp.Id, followUp.Status });

        followUp.Status = FollowUpStatus.Sent;
        followUp.SentAt = DateTime.UtcNow;
        if (followUp.Lead.Stage == LeadStage.New)
        {
            followUp.Lead.Stage = LeadStage.Contacted;
            followUp.Lead.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return Ok(new { followUp.Id, followUp.Status });
    }

    /// <summary>
    /// Intake for forms/webhooks routed through n8n. Goes to the original business unless
    /// ?businessId= names another one.
    /// </summary>
    [HttpPost("inquiries")]
    public async Task<IActionResult> Intake(CreateInquiryRequest req, [FromQuery] int? businessId, CancellationToken ct)
    {
        var target = businessId ?? await db.DefaultBusinessIdAsync(ct);
        if (target <= 0 || !await db.BusinessProfiles.IgnoreQueryFilters().AnyAsync(p => p.Id == target, ct))
            return NotFound(new { error = "Unknown business." });

        tenant.Use(target); // from here on, every query and insert is scoped to that business
        var inquiry = await svc.CreateAsync(req, ct);
        return Ok(new { inquiry.Id, inquiry.Status, inquiry.Category, inquiry.Urgency, inquiry.LeadId });
    }
}
