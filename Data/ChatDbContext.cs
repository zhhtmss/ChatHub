using Microsoft.EntityFrameworkCore;
using WebApplication24.Models;

namespace WebApplication24.Data;

public class ChatDbContext(DbContextOptions<ChatDbContext> options) : DbContext(options)
{
    public DbSet<ChatUser> Users => Set<ChatUser>();
    public DbSet<PrivateMessage> Messages => Set<PrivateMessage>();
    public DbSet<GeneralMessage> GeneralMessages => Set<GeneralMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChatUser>().HasIndex(u => u.NormalizedUsername).IsUnique();
        modelBuilder.Entity<PrivateMessage>().HasIndex(m => new { m.SenderId, m.RecipientId, m.SentAt });
        modelBuilder.Entity<PrivateMessage>().Property(m => m.Text).HasMaxLength(4000);
        modelBuilder.Entity<GeneralMessage>().HasIndex(m => m.SentAt);
        modelBuilder.Entity<GeneralMessage>().Property(m => m.Text).HasMaxLength(4000);
    }
}
