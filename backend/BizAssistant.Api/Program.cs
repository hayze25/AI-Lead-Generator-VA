using System.Text.Json.Serialization;
using BizAssistant.Api.Data;
using BizAssistant.Api.Models;
using BizAssistant.Api.Services;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ---- Login ----------------------------------------------------------------
// Refuse to start with missing or weak credentials rather than run an unprotected app.
var authUser = builder.Configuration["Auth:Username"] ?? "";
var authPassword = builder.Configuration["Auth:Password"] ?? "";
var jwtKey = builder.Configuration["Auth:JwtKey"] ?? "";
if (authUser.Length == 0 || authPassword.Length < 8 || jwtKey.Length < 32)
{
    throw new InvalidOperationException(
        "Login is not configured. Set Auth__Username, Auth__Password (at least 8 characters) and " +
        "Auth__JwtKey (at least 32 random characters). See the README.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = BizAssistant.Api.Controllers.AuthController.Issuer,
        ValidateAudience = true,
        ValidAudience = BizAssistant.Api.Controllers.AuthController.Issuer,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.FromMinutes(1)
    });

// Every endpoint requires a signed-in user unless it is marked [AllowAnonymous].
builder.Services.AddAuthorization(o =>
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection("Llm"));
builder.Services.Configure<N8nOptions>(builder.Configuration.GetSection("N8n"));

builder.Services.AddHttpClient<ILlmService, LlmService>(c => c.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddHttpClient<IAutomationNotifier, N8nNotifier>(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<InquiryService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentTenant, CurrentTenant>();

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    // Entities reference each other (Lead <-> Inquiry); don't blow up when serializing them.
    o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});

var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Turn failures of external services (LLM, n8n) into a clean 502 the UI can display.
// Anything unexpected becomes a generic 500 so internals are never leaked to the client.
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();

    if (error is IntegrationException integration)
    {
        context.Response.StatusCode = StatusCodes.Status502BadGateway;
        await context.Response.WriteAsJsonAsync(new { error = integration.Message });
        return;
    }

    logger.LogError(error, "Unhandled exception");
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new { error = "Something went wrong on the server." });
}));

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

if (app.Configuration.GetValue("Database:AutoCreate", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // SQL Server in Docker can take a while to accept connections - retry instead of crashing.
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            db.Database.EnsureCreated();
            // EnsureCreated never alters existing tables, so upgrade databases from earlier versions by hand.
            // Every statement is idempotent: on a fresh database they all do nothing.
            foreach (var sql in UpgradeScripts)
                db.Database.ExecuteSqlRaw(sql);
            break;
        }
        catch (Exception ex) when (attempt < 15)
        {
            app.Logger.LogWarning("Database not ready (attempt {Attempt}/15): {Message}", attempt, ex.Message);
            Thread.Sleep(TimeSpan.FromSeconds(3));
        }
    }

    // IgnoreQueryFilters: there is no signed-in business at startup, so the tenant filter would hide every row.
    if (!db.BusinessProfiles.IgnoreQueryFilters().Any())
    {
        db.BusinessProfiles.Add(new BusinessProfile
        {
            Name = "Sample Business",
            Description = "A small local business. Edit this profile in Settings so the AI knows what you sell.",
            Tone = "Friendly and professional",
            Faq = "Opening hours: Mon-Fri 9am-5pm. We reply within one business day."
        });
        db.SaveChanges();
    }
}

app.Run();

public partial class Program
{
    // Brings a database created by an earlier version up to date. Order matters.
    private static readonly string[] UpgradeScripts =
    [
        // v2: business logo
        "IF COL_LENGTH('dbo.BusinessProfiles','LogoDataUrl') IS NULL " +
        "ALTER TABLE dbo.BusinessProfiles ADD LogoDataUrl NVARCHAR(MAX) NULL;",

        // v3: registered users
        "IF OBJECT_ID('dbo.Users','U') IS NULL " +
        "CREATE TABLE dbo.Users (" +
        "Id INT IDENTITY(1,1) PRIMARY KEY, Email NVARCHAR(320) NOT NULL, PasswordHash NVARCHAR(400) NOT NULL, " +
        "BusinessId INT NOT NULL, CreatedAt DATETIME2 NOT NULL);",
        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Users_Email' AND object_id = OBJECT_ID('dbo.Users')) " +
        "CREATE UNIQUE INDEX IX_Users_Email ON dbo.Users (Email);",

        // v3: existing leads and inquiries all belong to the original business (lowest id)
        "IF COL_LENGTH('dbo.Leads','BusinessId') IS NULL BEGIN " +
        "ALTER TABLE dbo.Leads ADD BusinessId INT NULL; " +
        "EXEC('UPDATE dbo.Leads SET BusinessId = (SELECT MIN(Id) FROM dbo.BusinessProfiles)'); " +
        "EXEC('ALTER TABLE dbo.Leads ALTER COLUMN BusinessId INT NOT NULL'); END",
        "IF COL_LENGTH('dbo.Inquiries','BusinessId') IS NULL BEGIN " +
        "ALTER TABLE dbo.Inquiries ADD BusinessId INT NULL; " +
        "EXEC('UPDATE dbo.Inquiries SET BusinessId = (SELECT MIN(Id) FROM dbo.BusinessProfiles)'); " +
        "EXEC('ALTER TABLE dbo.Inquiries ALTER COLUMN BusinessId INT NOT NULL'); END",

        // v3: a lead's email is now unique per business instead of globally
        "IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Leads_Email' AND object_id = OBJECT_ID('dbo.Leads')) " +
        "DROP INDEX IX_Leads_Email ON dbo.Leads;",
        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Leads_BusinessId_Email' AND object_id = OBJECT_ID('dbo.Leads')) " +
        "CREATE UNIQUE INDEX IX_Leads_BusinessId_Email ON dbo.Leads (BusinessId, Email);",
        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Inquiries_BusinessId' AND object_id = OBJECT_ID('dbo.Inquiries')) " +
        "CREATE INDEX IX_Inquiries_BusinessId ON dbo.Inquiries (BusinessId);",
    ];
}
