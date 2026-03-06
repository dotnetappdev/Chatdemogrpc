namespace ChatApp.Shared.Models;

public class MessageDto
{
    public int Id { get; set; }
    public string FromUser { get; set; } = string.Empty;
    public string ToUser { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public bool IsRead { get; set; }
}

public class SaveMessageRequest
{
    public string FromUser { get; set; } = string.Empty;
    public string ToUser { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class ConversationHistoryRequest
{
    public string User1 { get; set; } = string.Empty;
    public string User2 { get; set; } = string.Empty;
    public int PageSize { get; set; } = 50;
    public int Page { get; set; } = 1;
}
