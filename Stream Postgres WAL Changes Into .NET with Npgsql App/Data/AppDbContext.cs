using Microsoft.EntityFrameworkCore;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Models;

namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Data
{
    public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>(e =>
            {
                e.ToTable("Orders"); // 明确指定表名
                e.HasKey(o => o.Id);
                e.Property(o => o.CustomerName).IsRequired().HasMaxLength(100);
                e.Property(o => o.Amount).IsRequired().HasColumnType("decimal(18,2)");
                e.Property(o => o.Status).IsRequired().HasMaxLength(50);

                e.HasIndex(o => o.CreatedAt);
            });

            modelBuilder.Entity<OutboxEvent>(e =>
            {
                e.ToTable("OutboxEvents"); // 明确指定表名
                e.HasKey(oe => oe.Id);
                e.Property(oe => oe.AggregateType).IsRequired().HasMaxLength(100);
                e.Property(oe => oe.AggregateId).IsRequired().HasMaxLength(50);
                e.Property(oe => oe.EventType).IsRequired().HasMaxLength(100);
                e.Property(oe => oe.Payload).IsRequired();

                e.HasIndex(oe => new { oe.Processed, oe.CreatedAt });
                e.HasIndex(oe => oe.CreatedAt);
            });

            base.OnModelCreating(modelBuilder);
        }
    }
}