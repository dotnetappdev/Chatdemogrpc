using ChatApp.Data.Entities;

namespace ChatApp.Data.Repositories;

public interface IMessageRepository
{
    Task<Message> SaveAsync(Message message);
    Task<IEnumerable<Message>> GetConversationAsync(string user1, string user2, int page, int pageSize);
    Task<int> GetUnreadCountAsync(string toUser, string fromUser);
    Task MarkAsReadAsync(string toUser, string fromUser);
}
