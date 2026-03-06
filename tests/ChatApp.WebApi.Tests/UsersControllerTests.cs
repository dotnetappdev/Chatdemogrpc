using ChatApp.Data.Entities;
using ChatApp.Data.Repositories;
using ChatApp.Shared.Models;
using ChatApp.WebApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace ChatApp.WebApi.Tests;

public sealed class UsersControllerTests
{
    private readonly Mock<IUserRepository> _repoMock = new();
    private readonly UsersController       _sut;

    public UsersControllerTests() => _sut = new UsersController(_repoMock.Object);

    // ── GetAll ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_ReturnsOkWithAllUsers()
    {
        _repoMock.Setup(r => r.GetAllAsync())
            .ReturnsAsync(new[] { MakeUser("alice"), MakeUser("bob") });

        var result = await _sut.GetAll();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dtos = Assert.IsAssignableFrom<IEnumerable<UserDto>>(ok.Value);
        Assert.Equal(2, dtos.Count());
    }

    [Fact]
    public async Task GetAll_ReturnsEmptyList_WhenNoUsers()
    {
        _repoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(Array.Empty<User>());

        var result = await _sut.GetAll();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<UserDto>>(ok.Value));
    }

    // ── GetOnline ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOnline_ReturnsOnlyOnlineUsers()
    {
        _repoMock.Setup(r => r.GetAllOnlineAsync())
            .ReturnsAsync(new[] { MakeUser("alice", online: true) });

        var result = await _sut.GetOnline();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<UserDto>>(ok.Value));
    }

    // ── Get (single) ──────────────────────────────────────────────────────

    [Fact]
    public async Task Get_ReturnsUser_WhenFound()
    {
        _repoMock.Setup(r => r.GetByUserNameAsync("alice")).ReturnsAsync(MakeUser("alice"));

        var result = await _sut.Get("alice");

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("alice", ((UserDto)ok.Value!).UserName);
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WhenUserMissing()
    {
        _repoMock.Setup(r => r.GetByUserNameAsync(It.IsAny<string>()))
            .ReturnsAsync((User?)null);

        var result = await _sut.Get("nobody");

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── Register ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_ReturnsRegisteredUser()
    {
        _repoMock.Setup(r => r.UpsertAsync(It.IsAny<User>()))
            .ReturnsAsync((User u) => u);

        var req = new RegisterUserRequest
            { UserName = "alice", DisplayName = "Alice", IpAddress = "1.1.1.1", GrpcPort = 5000 };

        var result = await _sut.Register(req);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("alice", ((UserDto)ok.Value!).UserName);
    }

    [Fact]
    public async Task Register_UsesUserName_AsDisplayName_WhenDisplayNameBlank()
    {
        _repoMock.Setup(r => r.UpsertAsync(It.IsAny<User>()))
            .ReturnsAsync((User u) => u);

        var req = new RegisterUserRequest
            { UserName = "alice", DisplayName = "", IpAddress = "1.1.1.1", GrpcPort = 5000 };

        var result = await _sut.Register(req);

        var ok    = Assert.IsType<OkObjectResult>(result.Result);
        var dto   = (UserDto)ok.Value!;
        Assert.Equal("alice", dto.DisplayName);
    }

    // ── UpdateStatus ──────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateStatus_ReturnsNoContent_WhenSuccessful()
    {
        _repoMock.Setup(r => r.UpdateStatusAsync("alice", It.IsAny<int>(), null, null))
            .ReturnsAsync(true);

        var result = await _sut.UpdateStatus("alice",
            new UpdateStatusRequest { Status = UserStatus.Away });

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task UpdateStatus_ReturnsNotFound_WhenUserMissing()
    {
        _repoMock.Setup(r => r.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<int>(), null, null))
            .ReturnsAsync(false);

        var result = await _sut.UpdateStatus("ghost",
            new UpdateStatusRequest { Status = UserStatus.Away });

        Assert.IsType<NotFoundResult>(result);
    }

    // ── Delete ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ReturnsNoContent_WhenSuccessful()
    {
        _repoMock.Setup(r => r.DeleteAsync("alice")).ReturnsAsync(true);

        var result = await _sut.Delete("alice");
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenUserMissing()
    {
        _repoMock.Setup(r => r.DeleteAsync("ghost")).ReturnsAsync(false);

        var result = await _sut.Delete("ghost");
        Assert.IsType<NotFoundResult>(result);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static User MakeUser(string name, bool online = true) => new()
    {
        UserName    = name,
        DisplayName = char.ToUpperInvariant(name[0]) + name[1..],
        IpAddress   = "127.0.0.1",
        GrpcPort    = 5000,
        Status      = online ? 1 : 0,
        CreatedAt   = DateTime.UtcNow,
        LastSeen    = DateTime.UtcNow
    };
}
