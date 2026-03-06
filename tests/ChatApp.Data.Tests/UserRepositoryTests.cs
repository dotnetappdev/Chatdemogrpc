using ChatApp.Data.Entities;
using ChatApp.Data.Repositories;

namespace ChatApp.Data.Tests;

public sealed class UserRepositoryTests : DataTestBase
{
    private readonly UserRepository _sut;

    public UserRepositoryTests() => _sut = new UserRepository(Db);

    [Fact]
    public async Task Upsert_InsertsNewUser()
    {
        var user = new User { UserName = "alice", DisplayName = "Alice", IpAddress = "10.0.0.1", GrpcPort = 5000, Status = 1 };
        await _sut.UpsertAsync(user);

        var found = await _sut.GetByUserNameAsync("alice");
        Assert.NotNull(found);
        Assert.Equal("Alice", found.DisplayName);
    }

    [Fact]
    public async Task Upsert_UpdatesExistingUser()
    {
        await _sut.UpsertAsync(new User { UserName = "alice", DisplayName = "Alice", IpAddress = "10.0.0.1", GrpcPort = 5000, Status = 1 });
        await _sut.UpsertAsync(new User { UserName = "alice", DisplayName = "Alice W", IpAddress = "10.0.0.2", GrpcPort = 6000, Status = 1 });

        var all = await _sut.GetAllAsync();
        Assert.Single(all);
        Assert.Equal("Alice W", all.First().DisplayName);
        Assert.Equal("10.0.0.2", all.First().IpAddress);
    }

    [Fact]
    public async Task GetByUserName_ReturnsNull_ForUnknownUser()
    {
        var result = await _sut.GetByUserNameAsync("ghost");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllOnline_ReturnsOnlyOnlineUsers()
    {
        await _sut.UpsertAsync(new User { UserName = "alice", DisplayName = "A", IpAddress = "1.1.1.1", GrpcPort = 1, Status = 1 });
        await _sut.UpsertAsync(new User { UserName = "bob",   DisplayName = "B", IpAddress = "1.1.1.2", GrpcPort = 2, Status = 0 });

        var online = await _sut.GetAllOnlineAsync();
        Assert.Single(online);
        Assert.Equal("alice", online.First().UserName);
    }

    [Fact]
    public async Task UpdateStatus_ChangesUserStatus()
    {
        await _sut.UpsertAsync(new User { UserName = "alice", DisplayName = "A", IpAddress = "1.1.1.1", GrpcPort = 1, Status = 1 });
        var ok = await _sut.UpdateStatusAsync("alice", 0, null, null);

        Assert.True(ok);
        var user = await _sut.GetByUserNameAsync("alice");
        Assert.Equal(0, user!.Status);
    }

    [Fact]
    public async Task UpdateStatus_ReturnsFalse_ForUnknownUser()
    {
        var ok = await _sut.UpdateStatusAsync("nobody", 1, null, null);
        Assert.False(ok);
    }

    [Fact]
    public async Task Delete_RemovesUser()
    {
        await _sut.UpsertAsync(new User { UserName = "alice", DisplayName = "A", IpAddress = "1.1.1.1", GrpcPort = 1, Status = 1 });
        var ok = await _sut.DeleteAsync("alice");

        Assert.True(ok);
        Assert.Null(await _sut.GetByUserNameAsync("alice"));
    }

    [Fact]
    public async Task Delete_ReturnsFalse_ForUnknownUser()
    {
        var ok = await _sut.DeleteAsync("ghost");
        Assert.False(ok);
    }
}
