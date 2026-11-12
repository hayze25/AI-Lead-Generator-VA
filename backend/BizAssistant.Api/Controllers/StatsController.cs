using BizAssistant.Api.Data;
using BizAssistant.Api.Models;
using BizAssistant.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BizAssistant.Api.Controllers;

[ApiController]
[Route("api/stats")]
public class StatsController(AppDbContext db, IOptions<LlmOptions> llm) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var total = await db.Inquiries.CountAsync(ct);
        var open = await db.Inquiries.CountAsync(i => i.Status == InquiryStatus.New || i.Status == InquiryStatus.Triaged, ct);
        var leads = await db.Leads.CountAsync(ct);
        var pending = await db.FollowUps.CountAsync(f => f.Status == FollowUpStatus.Pending, ct);

        var categories = await db.Inquiries
            .Where(i => i.Category != null)
            .GroupBy(i => i.Category)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var stages = await db.Leads
            .GroupBy(l => l.Stage)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Group the last 7 days in memory: tiny result set, and it keeps the SQL simple.
        var since = DateTime.UtcNow.Date.AddDays(-6);
        var recent = await db.Inquiries
            .Where(i => i.ReceivedAt >= since)
            .Select(i => i.ReceivedAt)
            .ToListAsync(ct);

        var daily = Enumerable.Range(0, 7)
            .Select(n => since.AddDays(n))
            .Select(day => new { date = day.ToString("yyyy-MM-dd"), count = recent.Count(r => r.Date == day) })
            .ToList();

        return Ok(new
        {
            totalInquiries = total,
            openInquiries = open,
            totalLeads = leads,
            pendingFollowUps = pending,
            llmMode = string.IsNullOrWhiteSpace(llm.Value.ApiKey) ? "demo" : "live",
            byCategory = categories
                .OrderByDescending(c => c.Count)
                .Select(c => new { name = c.Key.ToString(), count = c.Count }),
            byStage = Enum.GetValues<LeadStage>()
                .Select(s => new { name = s.ToString(), count = stages.FirstOrDefault(x => x.Key == s)?.Count ?? 0 }),
            daily
        });
    }
}
