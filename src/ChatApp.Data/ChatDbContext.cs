using ChatApp.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Data;

public class ChatDbContext : DbContext
{
    public ChatDbContext(DbContextOptions<ChatDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Message> Messages => Set<Message>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.HasIndex(u => u.UserName).IsUnique();
            entity.Property(u => u.UserName).HasMaxLength(50).IsRequired();
            entity.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();
            entity.Property(u => u.IpAddress).HasMaxLength(45);
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.Property(m => m.FromUser).HasMaxLength(50).IsRequired();
            entity.Property(m => m.ToUser).HasMaxLength(50).IsRequired();
            entity.Property(m => m.Content).HasMaxLength(4000).IsRequired();

            entity.HasOne(m => m.Sender)
                  .WithMany(u => u.SentMessages)
                  .HasForeignKey(m => m.FromUserId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(m => m.Recipient)
                  .WithMany(u => u.ReceivedMessages)
                  .HasForeignKey(m => m.ToUserId)
                  .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
