using BizAssistant.Api.Data;
using BizAssistant.Api.Models;
using BizAssistant.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BizAssistant.Api.Controllers;

[ApiController]
[Route("api/inquiries")]
public class InquiriesController(AppDbContext db, InquiryService svc) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] InquiryStatus? status, [FromQuery] InquiryCategory? category, CancellationToken ct)
    {
        var q = db.Inquiries.AsNoTracking().AsQueryable();
        if (status.HasValue) q = q.Where(i => i.Status == status.Value);
        if (category.HasValue) q = q.Where(i => i.Category == category.Value);

        return Ok(await q.OrderByDescending(i => i.ReceivedAt).Take(200).ToListAsync(ct));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var inquiry = await db.Inquiries.AsNoTracking()
            .Include(i => i.Replies.OrderBy(r => r.CreatedAt))
            .Include(i => i.Lead)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

        return inquiry is null ? NotFound(new { error = "Inquiry not found." }) : Ok(inquiry);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateInquiryRequest req, CancellationToken ct)
    {
        var inquiry = await svc.CreateAsync(req, ct);
        return CreatedAtAction(nameof(Get), new { id = inquiry.Id }, inquiry);
    }

    [HttpPost("{id:int}/triage")]
    public async Task<IActionResult> Triage(int id, CancellationToken ct)
    {
        var inquiry = await db.Inquiries.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inquiry is null) return NotFound(new { error = "Inquiry not found." });

        return Ok(await svc.TriageAsync(inquiry, ct));
    }

    [HttpPost("{id:int}/replies/draft")]
    public async Task<IActionResult> DraftReply(int id, [FromBody] DraftReplyRequest? req, CancellationToken ct)
    {
        var reply = await svc.DraftReplyAsync(id, req?.Instruction, ct);
        return reply is null ? NotFound(new { error = "Inquiry not found." }) : Ok(reply);
    }

    [HttpPatch("{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, UpdateStatusRequest req, CancellationToken ct)
    {
        var inquiry = await db.Inquiries.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inquiry is null) return NotFound(new { error = "Inquiry not found." });

        inquiry.Status = req.Status;
        await db.SaveChangesAsync(ct);
        return Ok(inquiry);
    }
}
