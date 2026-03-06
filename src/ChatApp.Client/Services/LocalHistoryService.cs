using System.IO;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Client.Services;

/// <summary>
/// Lightweight local SQLite database for storing message history on this device.
/// Works even when the REST API is not running.
/// </summary>
public class LocalHistoryDb : DbContext
{
    private readonly string _dbPath;

    public LocalHistoryDb(string dbPath)
    {
        _dbPath = dbPath;
        Database.EnsureCreated();
    }

    public DbSet<LocalMessage> Messages => Set<LocalMessage>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseSqlite($"Data Source={_dbPath}");

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<LocalMessage>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.FromUser).HasMaxLength(50).IsRequired();
            e.Property(m => m.ToUser).HasMaxLength(50).IsRequired();
            e.Property(m => m.Content).HasMaxLength(4000).IsRequired();
            e.HasIndex(m => new { m.FromUser, m.ToUser });
        });
    }
}

public class LocalMessage
{
    public int Id { get; set; }
    public string FromUser { get; set; } = string.Empty;
    public string ToUser { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
}

/// <summary>Repository facade for the local SQLite message store.</summary>
public class LocalHistoryService : IDisposable
{
    private readonly LocalHistoryDb _db;

    public LocalHistoryService()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChatApp");
        Directory.CreateDirectory(folder);
        _db = new LocalHistoryDb(Path.Combine(folder, "history.db"));
    }

    public async Task SaveAsync(string fromUser, string toUser, string content)
    {
        _db.Messages.Add(new LocalMessage
        {
            FromUser = fromUser,
            ToUser = toUser,
            Content = content,
            SentAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    public async Task<List<LocalMessage>> GetConversationAsync(
        string user1, string user2, int count = 100)
    {
        return await _db.Messages
            .Where(m =>
                (m.FromUser == user1 && m.ToUser == user2) ||
                (m.FromUser == user2 && m.ToUser == user1))
            .OrderByDescending(m => m.SentAt)
            .Take(count)
            .OrderBy(m => m.SentAt)
            .ToListAsync();
    }

    public void Dispose() => _db.Dispose();
}
