namespace ChatApp.Core;

/// <summary>Holds the identity of the local user; set at login, cleared at logout.</summary>
public static class LocalUserContext
{
    public static string CurrentUserName    { get; set; } = string.Empty;
    public static string CurrentDisplayName { get; set; } = string.Empty;
}
