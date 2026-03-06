using System.IO;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Core.Services;

// ── Entity classes ────────────────────────────────────────────────────────

public class LocalMessageRow
{
    public int    Id       { get; set; }
    public string FromUser { get; set; } = string.Empty;
    public string ToUser   { get; set; } = string.Empty;
    public string Content  { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
}

public class KnownPeerRow
{
    public string   UserName    { get; set; } = string.Empty; // PK
    public string   DisplayName { get; set; } = string.Empty;
    public string   IpAddress   { get; set; } = string.Empty;
    public int      GrpcPort    { get; set; }
    public DateTime LastSeenUtc { get; set; }
}

// ── DbContext ─────────────────────────────────────────────────────────────

/// <summary>
/// Single local SQLite database that stores message history and the known-peers
/// list. Accepts <see cref="DbContextOptions{LocalDb}"/> so tests can substitute
/// an in-memory connection without touching the file system.
/// </summary>
public class LocalDb : DbContext
{
    public LocalDb(DbContextOptions<LocalDb> options) : base(options) { }

    public DbSet<LocalMessageRow> Messages   => Set<LocalMessageRow>();
    public DbSet<KnownPeerRow>    KnownPeers => Set<KnownPeerRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<LocalMessageRow>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.FromUser).HasMaxLength(50).IsRequired();
            e.Property(m => m.ToUser).HasMaxLength(50).IsRequired();
            e.Property(m => m.Content).HasMaxLength(4000).IsRequired();
            e.HasIndex(m => new { m.FromUser, m.ToUser });
        });

        model.Entity<KnownPeerRow>(e =>
        {
            e.HasKey(p => p.UserName);
            e.Property(p => p.UserName).HasMaxLength(50).IsRequired();
            e.Property(p => p.DisplayName).HasMaxLength(100);
            e.Property(p => p.IpAddress).HasMaxLength(45);
        });
    }

    // ── Factory for production (file-based) ──────────────────────────────

    /// <summary>
    /// Creates a <see cref="LocalDb"/> backed by a SQLite file under
    /// <c>%LOCALAPPDATA%\ChatApp</c> (or the provided folder).
    /// </summary>
    public static LocalDb CreateForProduction(string? folderOverride = null)
    {
        var folder = folderOverride
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ChatApp");

        Directory.CreateDirectory(folder);

        var options = new DbContextOptionsBuilder<LocalDb>()
            .UseSqlite($"Data Source={Path.Combine(folder, "local.db")}")
            .Options;

        var db = new LocalDb(options);
        db.Database.EnsureCreated();
        return db;
    }
}
