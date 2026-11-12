using System.ComponentModel.DataAnnotations;

namespace BizAssistant.Api.Models;

public record CreateInquiryRequest(
    [param: Required, StringLength(200)] string CustomerName,
    [param: Required, EmailAddress, StringLength(320)] string CustomerEmail,
    [param: StringLength(50)] string? Channel,
    [param: StringLength(300)] string? Subject,
    [param: Required, StringLength(4000)] string Message);

public record DraftReplyRequest(
    [param: StringLength(500)] string? Instruction);

public record UpdateReplyRequest(
    [param: Required, StringLength(4000)] string Body);

public record UpdateStatusRequest(
    [param: Required] InquiryStatus Status);

public record UpdateLeadRequest(
    [param: Range(typeof(LeadStage), nameof(LeadStage), nameof(LeadStage))] LeadStage? Stage,
    [param: StringLength(2000)] string? Notes);

public record DraftFollowUpRequest(
    [param: Range(0, 90)] int? InDays,
    [param: StringLength(500)] string? Context);

public record UpdateProfileRequest(
    [param: Required, StringLength(200)] string Name,
    [param: StringLength(2000)] string Description,
    [param: StringLength(100)] string Tone,
    [param: StringLength(4000)] string Faq,
    [param: StringLength(450000)] string? LogoDataUrl = null);

public record RegisterRequest(
    [param: Required, StringLength(200)] string BusinessName,
    [param: Required, EmailAddress, StringLength(320)] string Email,
    [param: Required, StringLength(200, MinimumLength = 8)] string Password);

public record LoginRequest(
    [param: Required, StringLength(320)] string Username,
    [param: Required, StringLength(200)] string Password);

/// <summary>Payload n8n receives for every outbound email.</summary>
public record EmailMessage(
    string To,
    string Subject,
    string Body,
    string Type,
    int RefId);

/// <summary>What n8n gets back from GET /api/automation/followups/due.</summary>
public record DueFollowUpDto(
    int Id,
    int LeadId,
    string Name,
    string To,
    string Subject,
    string Body);