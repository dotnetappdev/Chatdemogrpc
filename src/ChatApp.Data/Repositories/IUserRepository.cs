using ChatApp.Data.Entities;

namespace ChatApp.Data.Repositories;

public interface IUserRepository
{
    Task<User?> GetByUserNameAsync(string userName);
    Task<IEnumerable<User>> GetAllOnlineAsync();
    Task<IEnumerable<User>> GetAllAsync();
    Task<User> UpsertAsync(User user);
    Task<bool> UpdateStatusAsync(string userName, int status, string? ipAddress, int? grpcPort);
    Task<bool> DeleteAsync(string userName);
}
