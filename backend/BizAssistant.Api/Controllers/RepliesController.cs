using BizAssistant.Api.Data;
using BizAssistant.Api.Models;
using BizAssistant.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BizAssistant.Api.Controllers;

[ApiController]
[Route("api/replies")]
public class RepliesController(AppDbContext db, IAutomationNotifier notifier) : ControllerBase
{
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateReplyRequest req, CancellationToken ct)
    {
        var reply = await db.Replies.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (reply is null) return NotFound(new { error = "Reply not found." });
        if (reply.Status == ReplyStatus.Sent) return Conflict(new { error = "This reply was already sent and can't be edited." });

        reply.Body = req.Body.Trim();
        await db.SaveChangesAsync(ct);
        return Ok(reply);
    }

    /// <summary>
    /// A human always presses send: the AI only drafts. The reply is marked Sent only after n8n accepted the email.
    /// </summary>
    [HttpPost("{id:int}/send")]
    public async Task<IActionResult> Send(int id, CancellationToken ct)
    {
        var reply = await db.Replies
            .Include(r => r.Inquiry!).ThenInclude(i => i.Lead)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (reply?.Inquiry is null) return NotFound(new { error = "Reply not found." });
        if (reply.Status == ReplyStatus.Sent) return Conflict(new { error = "This reply was already sent." });

        var inquiry = reply.Inquiry;
        await notifier.SendEmailAsync(
            new EmailMessage(inquiry.CustomerEmail, $"Re: {inquiry.Subject}", reply.Body, "reply", reply.Id), ct);

        reply.Status = ReplyStatus.Sent;
        reply.SentAt = DateTime.UtcNow;
        inquiry.Status = InquiryStatus.Replied;

        if (inquiry.Lead is { Stage: LeadStage.New } lead)
        {
            lead.Stage = LeadStage.Contacted;
            lead.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return Ok(new { reply.Id, reply.Status, reply.SentAt });
    }
}
