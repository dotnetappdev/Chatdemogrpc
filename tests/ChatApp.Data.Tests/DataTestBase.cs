using ChatApp.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Data.Tests;

/// <summary>
/// Provides each Data test with a fresh in-memory SQLite ChatDbContext.
/// </summary>
public abstract class DataTestBase : IDisposable
{
    private readonly SqliteConnection _connection;
    protected readonly ChatDbContext Db;

    protected DataTestBase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ChatDbContext>()
            .UseSqlite(_connection)
            .Options;

        Db = new ChatDbContext(options);
        Db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
