using Microsoft.EntityFrameworkCore;

namespace VioletStudio.Web.Data;

public sealed class VioletDbContext(DbContextOptions<VioletDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<PurchaseOrder> Orders => Set<PurchaseOrder>();
    public DbSet<LicenseKey> LicenseKeys => Set<LicenseKey>();
    public DbSet<PurchaseMessage> PurchaseMessages => Set<PurchaseMessage>();
    public DbSet<DownloadRecord> DownloadRecords => Set<DownloadRecord>();
    public DbSet<AdCredit> AdCredits => Set<AdCredit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.HasIndex(x => x.Email).IsUnique();
            entity.Property(x => x.DisplayName).HasMaxLength(80).IsRequired();
            entity.Property(x => x.PasswordSalt).HasMaxLength(64);
            entity.Property(x => x.PasswordHash).HasMaxLength(128);
            entity.Property(x => x.ProfileImage).HasMaxLength(1024);
            entity.Property(x => x.Role).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Provider).HasMaxLength(30);
            entity.Property(x => x.ProviderSubject).HasMaxLength(200);
            entity.Property(x => x.DeviceId).HasMaxLength(120);
            entity.Property(x => x.CreatedAt);
        });

        modelBuilder.Entity<PurchaseOrder>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Amount).HasPrecision(10, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.PaymentReference).HasMaxLength(120);
            entity.Property(x => x.PlanCode).HasMaxLength(30);
            entity.Property(x => x.CreatedAt);
            entity.HasOne(x => x.User).WithMany(x => x.Orders).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.UserId, x.CreatedAt });
            entity.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<LicenseKey>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).HasMaxLength(40).IsRequired();
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.PlanCode).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Note).HasMaxLength(500);
            entity.Property(x => x.CreatedAt);
            entity.HasOne(x => x.RedeemedBy).WithMany().HasForeignKey(x => x.RedeemedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseMessage>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Contact).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Package).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.Property(x => x.CreatedAt);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.CreatedAt);
            entity.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<DownloadRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.VideoTitle).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Thumbnail).HasMaxLength(1024);
            entity.Property(x => x.Platform).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Resolution).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Format).HasMaxLength(20).IsRequired();
            entity.Property(x => x.SourceUrl).HasMaxLength(2048);
            entity.Property(x => x.CreatedAt);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.UserId, x.CreatedAt });
        });

        modelBuilder.Entity<AdCredit>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.UserId).IsUnique();
            entity.Property(x => x.CreatedAt);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public sealed class UserAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? ProfileImage { get; set; }
    public string? PasswordSalt { get; set; }
    public string? PasswordHash { get; set; }
    public int PasswordIterations { get; set; } = 210_000;
    public string Role { get; set; } = "USER"; // USER | ADMIN
    public string Status { get; set; } = "ACTIVE"; // ACTIVE | SUSPENDED
    public string? Provider { get; set; } // local | google
    public string? ProviderSubject { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProExpiresAt { get; set; } // null with ProLifetime means lifetime
    public bool ProLifetime { get; set; }
    public string? DeviceId { get; set; }
    public List<PurchaseOrder> Orders { get; set; } = [];
}

public sealed class PurchaseOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public UserAccount User { get; set; } = null!;
    public int DurationMonths { get; set; } // 0 for lifetime
    public string? PlanCode { get; set; } // PRO_1M, PRO_5M, LIFETIME
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "TRY";
    public string Status { get; set; } = "PendingPayment";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PaidAt { get; set; }
    public string? PaymentReference { get; set; }
}

public sealed class LicenseKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string PlanCode { get; set; } = "PRO_1M"; // PRO_1M, PRO_5M, LIFETIME
    public int Months { get; set; } // 0 for lifetime
    public string Status { get; set; } = "Active"; // Active, Redeemed, Revoked
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RedeemedAt { get; set; }
    public Guid? RedeemedByUserId { get; set; }
    public UserAccount? RedeemedBy { get; set; }
}

public sealed class PurchaseMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public UserAccount User { get; set; } = null!;
    public string Contact { get; set; } = "";
    public string Package { get; set; } = "";
    public string Message { get; set; } = "";
    public string Status { get; set; } = "NEW"; // NEW, REVIEWING, COMPLETED
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DownloadRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public UserAccount User { get; set; } = null!;
    public string VideoTitle { get; set; } = "";
    public string? Thumbnail { get; set; }
    public string Platform { get; set; } = "";
    public string Resolution { get; set; } = "";
    public string Format { get; set; } = "";
    public string? SourceUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AdCredit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public UserAccount User { get; set; } = null!;
    public int CompletedTasks { get; set; }
    public int AvailableCredits { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
