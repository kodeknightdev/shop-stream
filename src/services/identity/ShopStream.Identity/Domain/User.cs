namespace ShopStream.Identity.Domain;

public sealed class User
{
    private User() { }

    public User(string email, string fullName, string passwordHash, string role, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        Email = email.Trim().ToLowerInvariant();
        FullName = fullName.Trim();
        PasswordHash = passwordHash;
        Role = role;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string Role { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }

    public List<RefreshToken> RefreshTokens { get; private set; } = new();

    public void RecordLogin() => LastLoginAt = DateTimeOffset.UtcNow;

    public void ChangeRole(string role) => Role = role;
}
