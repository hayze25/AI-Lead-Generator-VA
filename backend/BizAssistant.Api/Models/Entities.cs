namespace BizAssistant.Api.Models;

public enum InquiryStatus { New, Triaged, Replied, Closed }
public enum InquiryCategory { Sales, Support, Billing, Partnership, Spam, Other }
public enum Sentiment { Positive, Neutral, Negative }
public enum Urgency { Low, Medium, High }
public enum LeadStage { New, Contacted, Qualified, Won, Lost }
public enum ReplyStatus { Draft, Sent }
public enum FollowUpStatus { Pending, Sent, Cancelled }

/// <summary>What the AI knows about the business. Single row for now (single-tenant).</summary>
public class BusinessProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Tone { get; set; } = "";
    public string Faq { get; set; } = "";
    /// <summary>Logo as a small "data:image/...;base64,..." URL (resized in the browser before upload).</summary>
    public string? LogoDataUrl { get; set; }
}

/// <summary>A registered login. Each user belongs to exactly one business. (The config-based admin is not stored here.)</summary>
public class AppUser
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public int BusinessId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Lead
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public string Source { get; set; } = "Web";
    public LeadStage Stage { get; set; } = LeadStage.New;
    public int Score { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<Inquiry> Inquiries { get; set; } = new();
    public List<FollowUp> FollowUps { get; set; } = new();
}

public class Inquiry
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public string CustomerName { get; set; } = "";
    public string CustomerEmail { get; set; } = "";
    public string Channel { get; set; } = "Web";
    public string Subject { get; set; } = "";
    public string Message { get; set; } = "";

    // Filled in by AI triage
    public InquiryCategory? Category { get; set; }
    public Sentiment? Sentiment { get; set; }
    public Urgency? Urgency { get; set; }
    public string? Summary { get; set; }

    public InquiryStatus Status { get; set; } = InquiryStatus.New;
    public DateTime ReceivedAt { get; set; }

    public int? LeadId { get; set; }
    public Lead? Lead { get; set; }
    public List<Reply> Replies { get; set; } = new();
}

public class Reply
{
    public int Id { get; set; }
    public int InquiryId { get; set; }
    public Inquiry? Inquiry { get; set; }
    public string Body { get; set; } = "";
    public bool IsAiDraft { get; set; }
    public ReplyStatus Status { get; set; } = ReplyStatus.Draft;
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
}

public class FollowUp
{
    public int Id { get; set; }
    public int LeadId { get; set; }
    public Lead Lead { get; set; } = null!;
    public DateTime DueAt { get; set; }
    public string Message { get; set; } = "";
    public FollowUpStatus Status { get; set; } = FollowUpStatus.Pending;
    public DateTime? SentAt { get; set; }
}
