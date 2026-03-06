using ChatApp.Data.Entities;
using ChatApp.Data.Repositories;
using ChatApp.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace ChatApp.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserRepository _users;

    public UsersController(IUserRepository users) => _users = users;

    /// <summary>Get all registered users</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetAll()
    {
        var users = await _users.GetAllAsync();
        return Ok(users.Select(Map));
    }

    /// <summary>Get only online users</summary>
    [HttpGet("online")]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetOnline()
    {
        var users = await _users.GetAllOnlineAsync();
        return Ok(users.Select(Map));
    }

    /// <summary>Get a specific user by username</summary>
    [HttpGet("{userName}")]
    public async Task<ActionResult<UserDto>> Get(string userName)
    {
        var user = await _users.GetByUserNameAsync(userName);
        if (user is null) return NotFound();
        return Ok(Map(user));
    }

    /// <summary>Register or update a user (upsert)</summary>
    [HttpPost("register")]
    public async Task<ActionResult<UserDto>> Register([FromBody] RegisterUserRequest request)
    {
        var user = new User
        {
            UserName = request.UserName,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? request.UserName
                : request.DisplayName,
            IpAddress = request.IpAddress,
            GrpcPort = request.GrpcPort,
            Status = (int)UserStatus.Online
        };

        var saved = await _users.UpsertAsync(user);
        return Ok(Map(saved));
    }

    /// <summary>Update a user's online status and/or endpoint</summary>
    [HttpPut("{userName}/status")]
    public async Task<IActionResult> UpdateStatus(string userName, [FromBody] UpdateStatusRequest request)
    {
        var updated = await _users.UpdateStatusAsync(
            userName,
            (int)request.Status,
            request.IpAddress,
            request.GrpcPort);

        if (!updated) return NotFound();
        return NoContent();
    }

    /// <summary>Unregister / go offline</summary>
    [HttpDelete("{userName}")]
    public async Task<IActionResult> Delete(string userName)
    {
        var deleted = await _users.DeleteAsync(userName);
        if (!deleted) return NotFound();
        return NoContent();
    }

    private static UserDto Map(User u) => new()
    {
        Id = u.Id,
        UserName = u.UserName,
        DisplayName = u.DisplayName,
        IpAddress = u.IpAddress,
        GrpcPort = u.GrpcPort,
        Status = (UserStatus)u.Status,
        LastSeen = u.LastSeen
    };
}
