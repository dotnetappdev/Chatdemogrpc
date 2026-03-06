using ChatApp.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Core.Tests;

/// <summary>
/// Base class that provides every test with a fresh in-memory SQLite database
/// using a shared connection (required so the in-memory DB persists across
/// DbContext.SaveChanges calls within a single test).
/// </summary>
public abstract class LocalDbTestBase : IDisposable
{
    private readonly SqliteConnection _connection;
    protected readonly LocalDb Db;

    protected LocalDbTestBase()
    {
        // Keep the connection open so the :memory: database survives the test
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<LocalDb>()
            .UseSqlite(_connection)
            .Options;

        Db = new LocalDb(options);
        Db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
