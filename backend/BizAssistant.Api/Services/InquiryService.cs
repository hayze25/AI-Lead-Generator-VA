using BizAssistant.Api.Data;
using BizAssistant.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BizAssistant.Api.Services;

/// <summary>
/// The intake pipeline: save inquiry -> AI triage -> create/update lead -> schedule a follow-up.
/// Shared by the dashboard and the n8n intake webhook so both behave identically.
/// </summary>
public class InquiryService(AppDbContext db, ILlmService llm, ICurrentTenant tenant, ILogger<InquiryService> log)
{
    private const int DefaultFollowUpDays = 2;

    public async Task<BusinessProfile> GetProfileAsync(CancellationToken ct) =>
        // Project without the logo: the AI never needs it, and it can be a few hundred KB.
        await db.BusinessProfiles.AsNoTracking().OrderBy(p => p.Id)
            .Select(p => new BusinessProfile { Id = p.Id, Name = p.Name, Description = p.Description, Tone = p.Tone, Faq = p.Faq })
            .FirstOrDefaultAsync(ct)
        ?? new BusinessProfile { Name = "Our business", Tone = "Friendly and professional" };

    public async Task<Inquiry> CreateAsync(CreateInquiryRequest req, CancellationToken ct)
    {
        if (tenant.BusinessId <= 0)
            throw new InvalidOperationException("An inquiry can only be created inside a business.");

        var inquiry = new Inquiry
        {
            BusinessId = tenant.BusinessId,
            CustomerName = req.CustomerName.Trim(),
            CustomerEmail = req.CustomerEmail.Trim().ToLowerInvariant(),
            Channel = string.IsNullOrWhiteSpace(req.Channel) ? "Web" : req.Channel.Trim(),
            Subject = string.IsNullOrWhiteSpace(req.Subject) ? "(no subject)" : req.Subject.Trim(),
            Message = req.Message.Trim(),
            ReceivedAt = DateTime.UtcNow,
            Status = InquiryStatus.New
        };

        // Save first: the customer's message must never be lost because the AI call failed.
        db.Inquiries.Add(inquiry);
        await db.SaveChangesAsync(ct);

        await TriageAsync(inquiry, ct);
        return inquiry;
    }

    /// <summary>Runs AI triage. If the AI is unavailable the inquiry stays "New" and can be re-triaged later.</summary>
    public async Task<Inquiry> TriageAsync(Inquiry inquiry, CancellationToken ct)
    {
        var profile = await GetProfileAsync(ct);

        TriageResult result;
        try
        {
            result = await llm.TriageAsync(inquiry, profile, ct);
        }
        catch (IntegrationException ex)
        {
            log.LogWarning(ex, "Triage failed for inquiry {Id}; leaving it as New", inquiry.Id);
            return inquiry;
        }

        inquiry.Category = result.Category;
        inquiry.Sentiment = result.Sentiment;
        inquiry.Urgency = result.Urgency;
        inquiry.Summary = result.Summary;

        if (inquiry.Status == InquiryStatus.New)
            inquiry.Status = result.Category == InquiryCategory.Spam ? InquiryStatus.Closed : InquiryStatus.Triaged;

        if (result.IsLead && result.Category != InquiryCategory.Spam)
            await UpsertLeadAsync(inquiry, result.LeadScore, profile, ct);

        await db.SaveChangesAsync(ct);
        return inquiry;
    }

    private async Task UpsertLeadAsync(Inquiry inquiry, int score, BusinessProfile profile, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var email = inquiry.CustomerEmail;
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Email == email, ct);

        if (lead is null)
        {
            lead = new Lead
            {
                BusinessId = inquiry.BusinessId,
                Name = inquiry.CustomerName,
                Email = email,
                Source = inquiry.Channel,
                Stage = LeadStage.New,
                Score = score,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Leads.Add(lead);

            // A brand-new lead gets an automatic follow-up scheduled. n8n sends it when it falls due.
            try
            {
                var context = $"They enquired about \"{inquiry.Subject}\". {inquiry.Summary}".Trim();
                var message = await llm.DraftFollowUpAsync(lead, profile, context, ct);
                lead.FollowUps.Add(new FollowUp
                {
                    DueAt = now.AddDays(DefaultFollowUpDays),
                    Message = message,
                    Status = FollowUpStatus.Pending
                });
            }
            catch (IntegrationException ex)
            {
                log.LogWarning(ex, "Could not draft the automatic follow-up for {Email}; skipping it", email);
            }
        }
        else
        {
            lead.Score = Math.Max(lead.Score, score);
            lead.UpdatedAt = now;
        }

        inquiry.Lead = lead;
    }

    public async Task<Reply?> DraftReplyAsync(int inquiryId, string? instruction, CancellationToken ct)
    {
        var inquiry = await db.Inquiries.FirstOrDefaultAsync(i => i.Id == inquiryId, ct);
        if (inquiry is null) return null;

        var profile = await GetProfileAsync(ct);
        var body = await llm.DraftReplyAsync(inquiry, profile, instruction, ct);

        var reply = new Reply
        {
            InquiryId = inquiry.Id,
            Body = body,
            IsAiDraft = true,
            Status = ReplyStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };
        db.Replies.Add(reply);
        await db.SaveChangesAsync(ct);
        return reply;
    }

    public async Task<FollowUp?> DraftFollowUpAsync(int leadId, int inDays, string? context, CancellationToken ct)
    {
        var lead = await db.Leads
            .Include(l => l.Inquiries.OrderByDescending(i => i.ReceivedAt).Take(1))
            .FirstOrDefaultAsync(l => l.Id == leadId, ct);
        if (lead is null) return null;

        var profile = await GetProfileAsync(ct);
        var latest = lead.Inquiries.FirstOrDefault();
        var fallbackContext = latest is null
            ? null
            : $"Their last message was about \"{latest.Subject}\". {latest.Summary}".Trim();

        var message = await llm.DraftFollowUpAsync(lead, profile, string.IsNullOrWhiteSpace(context) ? fallbackContext : context, ct);

        var followUp = new FollowUp
        {
            LeadId = lead.Id,
            DueAt = DateTime.UtcNow.AddDays(inDays),
            Message = message,
            Status = FollowUpStatus.Pending
        };
        db.FollowUps.Add(followUp);
        await db.SaveChangesAsync(ct);
        return followUp;
    }
}
