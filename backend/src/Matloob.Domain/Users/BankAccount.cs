using Matloob.Domain.Common;

namespace Matloob.Domain.Users;

/// <summary>
/// A user's bank account (account holder name + IBAN + reference bank). The
/// legacy <c>bank_accounts</c> table was polymorphic; here it is scoped to a
/// <see cref="User"/> via <see cref="UserId"/> (establishments carry their own
/// bank details elsewhere). One active account per user.
/// </summary>
public sealed class BankAccount : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public Guid UserId { get; private set; }
    public Guid BankId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Iban { get; private set; } = string.Empty;

    private BankAccount() { }

    public BankAccount(Guid id, Guid userId, Guid bankId, string name, string iban)
    {
        Id = id;
        UserId = userId;
        BankId = bankId;
        Name = name;
        Iban = iban;
    }

    public void Update(Guid bankId, string name, string iban)
    {
        BankId = bankId;
        Name = name;
        Iban = iban;
    }
}
