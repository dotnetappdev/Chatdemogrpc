namespace ChatApp.Shared.Models;

public class UserDto
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int GrpcPort { get; set; }
    public UserStatus Status { get; set; }
    public DateTime LastSeen { get; set; }
}

public enum UserStatus
{
    Offline = 0,
    Online = 1,
    Away = 2,
    Busy = 3
}

public class RegisterUserRequest
{
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int GrpcPort { get; set; }
}

public class UpdateStatusRequest
{
    public UserStatus Status { get; set; }
    public string? IpAddress { get; set; }
    public int? GrpcPort { get; set; }
}
