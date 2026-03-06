using ChatApp.Shared.Models;

namespace ChatApp.Client.Services;

/// <summary>
/// REST API client for the ChatApp.WebApi - handles user directory and message history.
/// </summary>
public class ApiService : IDisposable
{
    private readonly HttpClient _http;
    private bool _disposed;

    public ApiService(string baseUrl = "http://localhost:5000")
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _http.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    // ─── Users ────────────────────────────────────────────────────────────

    public async Task<UserDto?> RegisterAsync(RegisterUserRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/users/register", request);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<UserDto>();
    }

    public async Task<List<UserDto>> GetOnlineUsersAsync()
    {
        try
        {
            var users = await _http.GetFromJsonAsync<List<UserDto>>("api/users/online");
            return users ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<UserDto?> GetUserAsync(string userName)
    {
        try
        {
            return await _http.GetFromJsonAsync<UserDto>($"api/users/{Uri.EscapeDataString(userName)}");
        }
        catch { return null; }
    }

    public async Task UpdateStatusAsync(string userName, UpdateStatusRequest request)
    {
        try
        {
            await _http.PutAsJsonAsync(
                $"api/users/{Uri.EscapeDataString(userName)}/status", request);
        }
        catch { /* best-effort */ }
    }

    public async Task UnregisterAsync(string userName)
    {
        try
        {
            await _http.DeleteAsync($"api/users/{Uri.EscapeDataString(userName)}");
        }
        catch { /* best-effort */ }
    }

    // ─── Messages ─────────────────────────────────────────────────────────

    public async Task<MessageDto?> SaveMessageAsync(SaveMessageRequest request)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/messages", request);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<MessageDto>();
        }
        catch { return null; }
    }

    public async Task<List<MessageDto>> GetConversationAsync(
        string user1, string user2, int page = 1, int pageSize = 50)
    {
        try
        {
            var url = $"api/messages/{Uri.EscapeDataString(user1)}/{Uri.EscapeDataString(user2)}" +
                      $"?page={page}&pageSize={pageSize}";
            var msgs = await _http.GetFromJsonAsync<List<MessageDto>>(url);
            return msgs ?? [];
        }
        catch { return []; }
    }

    public async Task MarkAsReadAsync(string toUser, string fromUser)
    {
        try
        {
            await _http.PostAsync(
                $"api/messages/markread/{Uri.EscapeDataString(toUser)}/{Uri.EscapeDataString(fromUser)}",
                null);
        }
        catch { /* best-effort */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}
