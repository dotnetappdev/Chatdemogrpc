namespace ChatApp.Data.Entities;

public class User
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int GrpcPort { get; set; }
    public int Status { get; set; } // maps to UserStatus enum
    public DateTime LastSeen { get; set; }
    public DateTime CreatedAt { get; set; }
    public ICollection<Message> SentMessages { get; set; } = [];
    public ICollection<Message> ReceivedMessages { get; set; } = [];
}
