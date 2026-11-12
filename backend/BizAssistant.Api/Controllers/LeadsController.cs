using BizAssistant.Api.Data;
using BizAssistant.Api.Models;
using BizAssistant.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BizAssistant.Api.Controllers;

[ApiController]
[Route("api/leads")]
public class LeadsController(AppDbContext db, InquiryService svc) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] LeadStage? stage, CancellationToken ct)
    {
        var q = db.Leads.AsNoTracking().AsQueryable();
        if (stage.HasValue) q = q.Where(l => l.Stage == stage.Value);

        var rows = await q
            .OrderByDescending(l => l.UpdatedAt)
            .Take(200)
            .Select(l => new
            {
                l.Id, l.Name, l.Email, l.Phone, l.Source, l.Stage, l.Score, l.Notes, l.CreatedAt, l.UpdatedAt,
                InquiryCount = l.Inquiries.Count,
                NextFollowUpAt = l.FollowUps
                    .Where(f => f.Status == FollowUpStatus.Pending)
                    .Min(f => (DateTime?)f.DueAt)
            })
            .ToListAsync(ct);

        return Ok(rows);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var lead = await db.Leads.AsNoTracking()
            .Include(l => l.Inquiries.OrderByDescending(i => i.ReceivedAt))
            .Include(l => l.FollowUps.OrderBy(f => f.DueAt))
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        return lead is null ? NotFound(new { error = "Lead not found." }) : Ok(lead);
    }

    [HttpPatch("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateLeadRequest req, CancellationToken ct)
    {
        var lead = await db.Leads.Include(l => l.FollowUps).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lead is null) return NotFound(new { error = "Lead not found." });

        if (req.Stage.HasValue)
        {
            lead.Stage = req.Stage.Value;

            // A closed deal (won or lost) shouldn't keep getting automated follow-ups.
            if (req.Stage is LeadStage.Won or LeadStage.Lost)
            {
                foreach (var f in lead.FollowUps.Where(f => f.Status == FollowUpStatus.Pending))
                    f.Status = FollowUpStatus.Cancelled;
            }
        }
        if (req.Notes is not null) lead.Notes = req.Notes;
        lead.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return Ok(new { lead.Id, lead.Stage, lead.Notes, lead.UpdatedAt });
    }

    [HttpPost("{id:int}/followups/draft")]
    public async Task<IActionResult> DraftFollowUp(int id, [FromBody] DraftFollowUpRequest? req, CancellationToken ct)
    {
        var followUp = await svc.DraftFollowUpAsync(id, req?.InDays ?? 2, req?.Context, ct);
        return followUp is null ? NotFound(new { error = "Lead not found." }) : Ok(followUp);
    }

    [HttpPost("{id:int}/followups/{followUpId:int}/cancel")]
    public async Task<IActionResult> CancelFollowUp(int id, int followUpId, CancellationToken ct)
    {
        var followUp = await db.FollowUps.FirstOrDefaultAsync(f => f.Id == followUpId && f.LeadId == id, ct);
        if (followUp is null) return NotFound(new { error = "Follow-up not found." });
        if (followUp.Status != FollowUpStatus.Pending) return Conflict(new { error = "Only pending follow-ups can be cancelled." });

        followUp.Status = FollowUpStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        return Ok(new { followUp.Id, followUp.Status });
    }
}
