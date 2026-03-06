namespace ChatApp.Data.Entities;

public class Message
{
    public int Id { get; set; }
    public string FromUser { get; set; } = string.Empty;
    public string ToUser { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public bool IsRead { get; set; }
    public int? FromUserId { get; set; }
    public User? Sender { get; set; }
    public int? ToUserId { get; set; }
    public User? Recipient { get; set; }
}
