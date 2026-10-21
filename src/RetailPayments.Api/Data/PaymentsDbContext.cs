using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RetailPayments.Api.Domain;

namespace RetailPayments.Api.Data;

public class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<RejectedRecord> RejectedRecords => Set<RejectedRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // SQLite stores decimals as text and can't sort them properly, so keep amounts as integer cents
        var cents = new ValueConverter<decimal, long>(
            amount => (long)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero),
            stored => stored / 100m);

        // read dates back as UTC
        var utc = new ValueConverter<DateTime, DateTime>(
            date => date.Kind == DateTimeKind.Utc ? date : date.ToUniversalTime(),
            stored => DateTime.SpecifyKind(stored, DateTimeKind.Utc));
        var utcNullable = new ValueConverter<DateTime?, DateTime?>(
            date => date == null ? date : (date.Value.Kind == DateTimeKind.Utc ? date : date.Value.ToUniversalTime()),
            stored => stored == null ? stored : DateTime.SpecifyKind(stored.Value, DateTimeKind.Utc));

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasIndex(t => new { t.SourceSystem, t.TransactionId }).IsUnique();
            entity.HasIndex(t => t.Status);
            entity.Property(t => t.SourceSystem).HasMaxLength(FieldLimits.Text).IsRequired();
            entity.Property(t => t.CardNumber).HasMaxLength(FieldLimits.Text).IsRequired();
            entity.Property(t => t.LocationCode).HasMaxLength(FieldLimits.Text).IsRequired();
            entity.Property(t => t.ProductName).HasMaxLength(FieldLimits.ProductName).IsRequired();
            entity.Property(t => t.Amount).HasConversion(cents).HasColumnType("INTEGER");
            entity.Property(t => t.TransactionTime).HasConversion(utc);
            entity.Property(t => t.FirstImportedAt).HasConversion(utc);
            entity.Property(t => t.LastUpdatedAt).HasConversion(utc);
            entity.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasMany(t => t.AuditEntries).WithOne(a => a.Transaction)
                .HasForeignKey(a => a.TransactionKey).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditEntry>(entity =>
        {
            entity.HasIndex(a => a.TransactionKey);
            entity.HasIndex(a => a.ImportBatchId);
            entity.Property(a => a.ChangeType).HasConversion<string>().HasMaxLength(20);
            entity.Property(a => a.FieldName).HasMaxLength(50);
            entity.Property(a => a.OldValue).HasMaxLength(200);
            entity.Property(a => a.NewValue).HasMaxLength(200);
            entity.Property(a => a.OccurredAt).HasConversion(utc);
            entity.HasOne(a => a.ImportBatch).WithMany().HasForeignKey(a => a.ImportBatchId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RejectedRecord>(entity =>
        {
            entity.HasIndex(r => r.ImportBatchId);
            entity.Property(r => r.SourceSystem).HasMaxLength(100);
            entity.Property(r => r.Reason).HasMaxLength(200);
            entity.Property(r => r.Payload).HasMaxLength(2000);
            entity.Property(r => r.RejectedAt).HasConversion(utc);
            entity.HasOne(r => r.ImportBatch).WithMany().HasForeignKey(r => r.ImportBatchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ImportBatch>(entity =>
        {
            entity.Property(b => b.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(b => b.TriggeredBy).HasMaxLength(50);
            entity.Property(b => b.StartedAt).HasConversion(utc);
            entity.Property(b => b.CompletedAt).HasConversion(utcNullable);
        });
    }
}
