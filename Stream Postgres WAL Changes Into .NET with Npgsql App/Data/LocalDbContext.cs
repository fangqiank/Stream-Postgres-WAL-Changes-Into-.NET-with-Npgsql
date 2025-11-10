using Microsoft.EntityFrameworkCore;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Models;

namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Data;

/// <summary>
/// DbContext for local PostgreSQL database that receives replication data from Neon
/// This context is used for UI operations and local data queries
/// </summary>
public class LocalDbContext : DbContext
{
    public LocalDbContext(DbContextOptions<LocalDbContext> options) : base(options)
    {
    }

    // Local tables that receive replicated data from Neon
    public DbSet<Order> Orders { get; set; }
    public DbSet<OutboxEvent> OutboxEvents { get; set; }
    public DbSet<LocalOutboxEvent> LocalOutboxEvents { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Order entity for local database to match source database schema
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders"); // Use PascalCase table name to match source database
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasColumnName("Id")
                .HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.CustomerName)
                .HasColumnName("CustomerName")
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(e => e.Amount)
                .HasColumnName("Amount")
                .HasPrecision(18, 2);
            entity.Property(e => e.Status)
                .HasColumnName("Status")
                .IsRequired()
                .HasMaxLength(50)
                .HasDefaultValue("Pending");
            entity.Property(e => e.CreatedAt)
                .HasColumnName("CreatedAt")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt)
                .HasColumnName("UpdatedAt");
        });

        // Configure OutboxEvent entity for local database to match source database schema
        modelBuilder.Entity<OutboxEvent>(entity =>
        {
            entity.ToTable("OutboxEvents"); // Use PascalCase table name to match source database
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasColumnName("Id")
                .HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.AggregateType)
                .HasColumnName("AggregateType")
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(e => e.AggregateId)
                .HasColumnName("AggregateId")
                .IsRequired()
                .HasMaxLength(50);
            entity.Property(e => e.EventType)
                .HasColumnName("EventType")
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(e => e.Payload)
                .HasColumnName("Payload")
                .IsRequired();
            entity.Property(e => e.Processed)
                .HasColumnName("Processed")
                .HasDefaultValue(false);
            entity.Property(e => e.ProcessedAt)
                .HasColumnName("ProcessedAt");
            entity.Property(e => e.CreatedAt)
                .HasColumnName("CreatedAt")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        // Configure LocalOutboxEvent entity for local database to match the schema
        modelBuilder.Entity<LocalOutboxEvent>(entity =>
        {
            entity.ToTable("outbox_events"); // Use lowercase table name
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasColumnName("id")
                .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(e => e.EventType)
                .HasColumnName("event_type")
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.EventData)
                .HasColumnName("event_data")
                .IsRequired();

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(e => e.ProcessedAt)
                .HasColumnName("processed_at")
                .IsRequired(false);

            entity.Property(e => e.RetryCount)
                .HasColumnName("retry_count")
                .HasDefaultValue(0);

            entity.Property(e => e.Status)
                .HasColumnName("status")
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue("Pending");
        });

        // Add indexes for better performance
        modelBuilder.Entity<Order>()
            .HasIndex(e => e.CreatedAt)
            .HasDatabaseName("idx_orders_created_at");

        modelBuilder.Entity<Order>()
            .HasIndex(e => e.Status)
            .HasDatabaseName("idx_orders_status");

        modelBuilder.Entity<OutboxEvent>()
            .HasIndex(e => new { e.Processed, e.CreatedAt })
            .HasDatabaseName("idx_outbox_events_processed_created_at");

        modelBuilder.Entity<OutboxEvent>()
            .HasIndex(e => e.CreatedAt)
            .HasDatabaseName("idx_outbox_events_created_at");

        modelBuilder.Entity<LocalOutboxEvent>()
            .HasIndex(e => e.CreatedAt)
            .HasDatabaseName("idx_local_outbox_events_created_at");
    }
}