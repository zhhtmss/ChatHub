namespace WebApplication24.Models;

public class GeneralMessage
{
    public int Id { get; set; }
    public int SenderId { get; set; }
    public required string Text { get; set; }
    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;
}
