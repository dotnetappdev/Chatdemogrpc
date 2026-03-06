using ChatApp.Core.Models;

namespace ChatApp.Core.Interfaces;

/// <summary>Local-first message history, stored in a SQLite database on this device.</summary>
public interface IHistoryService
{
    Task               SaveAsync(string fromUser, string toUser, string content);
    Task<List<ChatMessage>> GetConversationAsync(string user1, string user2,
        int count = 100, string localUser = "");
}
