namespace WebApplication24.Models;

public class ChatUser
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string NormalizedUsername { get; set; }
    public string PasswordHash { get; set; } = "";
}
