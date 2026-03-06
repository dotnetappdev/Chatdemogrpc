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

/// <summary>
/// Contact relationship with another user.
/// Stored locally — no server required.
/// </summary>
public class ContactRow
{
    /// <summary>The other user's username (PK).</summary>
    public string   UserName    { get; set; } = string.Empty;
    public string   DisplayName { get; set; } = string.Empty;
    /// <summary>0=Pending, 1=Accepted, 2=Blocked, 3=Declined</summary>
    public int      Status      { get; set; }
    /// <summary>True when the request came FROM the other user TO us.</summary>
    public bool     IsIncoming  { get; set; }
    public DateTime CreatedAt   { get; set; }
}

/// <summary>
/// Message queued for delivery to an offline peer.
/// Drained the next time that peer is reachable.
/// </summary>
public class PendingMessageRow
{
    public int      Id        { get; set; }
    public string   MessageId { get; set; } = string.Empty; // original gRPC message id
    public string   ToUser    { get; set; } = string.Empty;
    public string   FromUser  { get; set; } = string.Empty;
    public string   Content   { get; set; } = string.Empty;
    /// <summary>Matches <c>ChatApp.Shared.Grpc.MessageType</c> numeric value.</summary>
    public int      MessageType { get; set; }
    public DateTime QueuedAt  { get; set; }
    public int      Attempts  { get; set; }
}

// ── DbContext ─────────────────────────────────────────────────────────────

/// <summary>
/// Single local SQLite database that stores message history, known peers,
/// contact relationships, and the offline message queue.
/// Accepts <see cref="DbContextOptions{LocalDb}"/> so tests can substitute
/// an in-memory connection without touching the file system.
/// </summary>
public class LocalDb : DbContext
{
    public LocalDb(DbContextOptions<LocalDb> options) : base(options) { }

    public DbSet<LocalMessageRow>   Messages        => Set<LocalMessageRow>();
    public DbSet<KnownPeerRow>      KnownPeers      => Set<KnownPeerRow>();
    public DbSet<ContactRow>        Contacts        => Set<ContactRow>();
    public DbSet<PendingMessageRow> PendingMessages => Set<PendingMessageRow>();

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

        model.Entity<ContactRow>(e =>
        {
            e.HasKey(c => c.UserName);
            e.Property(c => c.UserName).HasMaxLength(50).IsRequired();
            e.Property(c => c.DisplayName).HasMaxLength(100);
        });

        model.Entity<PendingMessageRow>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.MessageId).HasMaxLength(64);
            e.Property(p => p.ToUser).HasMaxLength(50).IsRequired();
            e.Property(p => p.FromUser).HasMaxLength(50).IsRequired();
            e.Property(p => p.Content).HasMaxLength(4000).IsRequired();
            e.HasIndex(p => p.ToUser);
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
