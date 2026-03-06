using ChatApp.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Data.Repositories;

public class UserRepository : IUserRepository
{
    private readonly ChatDbContext _db;

    public UserRepository(ChatDbContext db) => _db = db;

    public async Task<User?> GetByUserNameAsync(string userName) =>
        await _db.Users.FirstOrDefaultAsync(u => u.UserName == userName);

    public async Task<IEnumerable<User>> GetAllOnlineAsync() =>
        await _db.Users.Where(u => u.Status != 0).OrderBy(u => u.DisplayName).ToListAsync();

    public async Task<IEnumerable<User>> GetAllAsync() =>
        await _db.Users.OrderBy(u => u.DisplayName).ToListAsync();

    public async Task<User> UpsertAsync(User user)
    {
        var existing = await _db.Users.FirstOrDefaultAsync(u => u.UserName == user.UserName);
        if (existing is null)
        {
            user.CreatedAt = DateTime.UtcNow;
            user.LastSeen = DateTime.UtcNow;
            _db.Users.Add(user);
        }
        else
        {
            existing.DisplayName = user.DisplayName;
            existing.IpAddress = user.IpAddress;
            existing.GrpcPort = user.GrpcPort;
            existing.Status = user.Status;
            existing.LastSeen = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
        return existing ?? user;
    }

    public async Task<bool> UpdateStatusAsync(string userName, int status, string? ipAddress, int? grpcPort)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserName == userName);
        if (user is null) return false;

        user.Status = status;
        user.LastSeen = DateTime.UtcNow;
        if (ipAddress is not null) user.IpAddress = ipAddress;
        if (grpcPort.HasValue) user.GrpcPort = grpcPort.Value;

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(string userName)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserName == userName);
        if (user is null) return false;

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
        return true;
    }
}
