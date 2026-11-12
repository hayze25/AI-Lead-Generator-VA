using BizAssistant.Api.Models;

namespace BizAssistant.Api.Services;

/// <summary>
/// Keyword-based stand-in used when no LLM API key is configured, so the whole
/// pipeline (triage, leads, drafts, follow-ups, n8n) can be demoed without any account.
/// It is deliberately simple - the real value comes from the LLM.
/// </summary>
public static class DemoLlm
{
    public static TriageResult Triage(Inquiry inquiry)
    {
        var text = $"{inquiry.Subject} {inquiry.Message}".ToLowerInvariant();
        bool Has(params string[] words) => words.Any(w => text.Contains(w));

        var category =
            Has("winner", "crypto", "click here", "seo service", "free money", "guaranteed ranking") ? InquiryCategory.Spam :
            Has("refund", "invoice", "charged", "billing", "payment failed", "receipt") ? InquiryCategory.Billing :
            Has("not working", "broken", "error", "issue", "problem", "can't log", "cannot log", "help") ? InquiryCategory.Support :
            Has("partner", "collaborat", "wholesale", "reseller", "sponsor") ? InquiryCategory.Partnership :
            Has("price", "pricing", "quote", "buy", "purchase", "interested", "demo", "how much", "proposal", "package") ? InquiryCategory.Sales :
            InquiryCategory.Other;

        var sentiment =
            Has("angry", "terrible", "worst", "disappointed", "unacceptable", "furious", "ridiculous") ? Sentiment.Negative :
            Has("thank", "great", "love", "awesome", "excellent", "amazing") ? Sentiment.Positive :
            Sentiment.Neutral;

        var urgency =
            Has("urgent", "asap", "immediately", "right away", "today") ? Urgency.High :
            (category is InquiryCategory.Sales or InquiryCategory.Billing or InquiryCategory.Support) ? Urgency.Medium :
            Urgency.Low;

        var isLead = category is InquiryCategory.Sales or InquiryCategory.Partnership;
        var score = !isLead ? 0 : Math.Min(100,
            (category == InquiryCategory.Sales ? 65 : 50) + (urgency == Urgency.High ? 15 : 0) + (sentiment == Sentiment.Positive ? 10 : 0));

        var summary = inquiry.Message.ReplaceLineEndings(" ").Trim().Truncate(140);
        return new TriageResult(category, sentiment, urgency, summary, isLead, score);
    }

    public static string Reply(Inquiry inquiry, BusinessProfile profile) =>
        $"Hi {inquiry.CustomerName.FirstName()},\n\n" +
        $"Thank you for getting in touch with {profile.Name}. We've received your message about \"{inquiry.Subject}\" " +
        "and a member of our team will get back to you within one business day.\n\n" +
        $"Kind regards,\n{profile.Name}";

    public static string FollowUp(Lead lead, BusinessProfile profile) =>
        $"Hi {lead.Name.FirstName()},\n\n" +
        $"I wanted to follow up on your recent enquiry with {profile.Name}. " +
        "Do you have any questions I can help with, or would you like to take the next step?\n\n" +
        $"Kind regards,\n{profile.Name}";
}
