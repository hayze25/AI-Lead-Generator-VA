using BizAssistant.Api.Models;
using BizAssistant.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BizAssistant.Api.Data;

public class AppDbContext : DbContext
{
    private readonly ICurrentTenant _tenant;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenant tenant) : base(options)
    {
        _tenant = tenant;
    }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<BusinessProfile> BusinessProfiles => Set<BusinessProfile>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<Inquiry> Inquiries => Set<Inquiry>();
    public DbSet<Reply> Replies => Set<Reply>();
    public DbSet<FollowUp> FollowUps => Set<FollowUp>();

    // Read by the query filters below. EF re-evaluates it for every query, so it always reflects the current request.
    private int TenantId => _tenant.BusinessId;

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.PasswordHash).HasMaxLength(400);
            e.HasIndex(x => x.Email).IsUnique();
        });

        // ---- Multi-tenancy -------------------------------------------------------------------
        // Every query on these tables is automatically restricted to the signed-in user's business.
        // Code that must see across businesses (n8n follow-up sender, login, startup) has to opt out
        // explicitly with IgnoreQueryFilters(), so forgetting a Where() can't leak another business's data.
        b.Entity<BusinessProfile>().HasQueryFilter(p => p.Id == TenantId);
        b.Entity<Lead>().HasQueryFilter(l => l.BusinessId == TenantId);
        b.Entity<Inquiry>().HasQueryFilter(i => i.BusinessId == TenantId);
        b.Entity<Reply>().HasQueryFilter(r => r.Inquiry!.BusinessId == TenantId);
        b.Entity<FollowUp>().HasQueryFilter(f => f.Lead.BusinessId == TenantId);

        b.Entity<BusinessProfile>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.Tone).HasMaxLength(100);
            e.Property(x => x.Faq).HasMaxLength(4000);
        });

        b.Entity<Lead>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.Phone).HasMaxLength(50);
            e.Property(x => x.Source).HasMaxLength(100);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.Stage).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.BusinessId, x.Email }).IsUnique(); // same email can be a lead in two different businesses
            e.HasIndex(x => x.Stage);
        });

        b.Entity<Inquiry>(e =>
        {
            e.Property(x => x.CustomerName).HasMaxLength(200);
            e.Property(x => x.CustomerEmail).HasMaxLength(320);
            e.Property(x => x.Channel).HasMaxLength(50);
            e.Property(x => x.Subject).HasMaxLength(300);
            e.Property(x => x.Message).HasMaxLength(4000);
            e.Property(x => x.Summary).HasMaxLength(1000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Sentiment).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Urgency).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.BusinessId);
            e.HasIndex(x => new { x.Status, x.ReceivedAt });
            e.HasOne(x => x.Lead).WithMany(l => l.Inquiries)
                .HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Reply>(e =>
        {
            e.Property(x => x.Body).HasMaxLength(4000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.Inquiry).WithMany(i => i.Replies)
                .HasForeignKey(x => x.InquiryId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<FollowUp>(e =>
        {
            e.Property(x => x.Message).HasMaxLength(4000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.Status, x.DueAt });
            e.HasOne(x => x.Lead).WithMany(l => l.FollowUps)
                .HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
        });

        // SQL Server returns DateTime with Kind=Unspecified, which the JSON serializer writes
        // without a "Z" - browsers would then read UTC values as local time. Mark them as UTC.
        var utc = new ValueConverter<DateTime, DateTime>(
            v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var utcNullable = new ValueConverter<DateTime?, DateTime?>(
            v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

        foreach (var entityType in b.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime)) property.SetValueConverter(utc);
                else if (property.ClrType == typeof(DateTime?)) property.SetValueConverter(utcNullable);
            }
        }
    }
}
