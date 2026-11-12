using CoreLedger.Domain.Exceptions;

namespace CoreLedger.Domain.Entities;

public class User
{
    public Guid Id { get; private set; }
    public string Email { get; private set; } = default!;
    public string FullName { get; private set; } = default!;
    public DateTime CreatedAt { get; private set; }

    private readonly List<Account> _accounts = new();
    public IReadOnlyCollection<Account> Accounts => _accounts.AsReadOnly();

    private User() { } // Для EF Core

    public User(string email, string fullName)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new DomainException("Email cannot be empty.");
        if (string.IsNullOrWhiteSpace(fullName)) throw new DomainException("Full name cannot be empty.");

        Id = Guid.NewGuid();
        Email = email.Trim().ToLowerInvariant();
        FullName = fullName.Trim();
        CreatedAt = DateTime.UtcNow;
    }
}
