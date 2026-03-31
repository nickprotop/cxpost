namespace CXPost.Models;

public class Contact
{
    public string Address { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public int UseCount { get; set; } = 1;
    public DateTime LastUsed { get; set; } = DateTime.UtcNow;
}
