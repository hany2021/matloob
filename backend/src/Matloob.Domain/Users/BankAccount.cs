using Matloob.Domain.Common;

namespace Matloob.Domain.Users;

/// <summary>
/// A bank account (account holder name + IBAN + reference bank). The legacy
/// <c>bank_accounts</c> table was polymorphic; here the account is ownerless and
/// each owner references it via an owner-side FK — <see cref="User.BankAccountId"/>
/// and <see cref="Matloob.Domain.Establishments.Establishment.BankAccountId"/>.
/// A given row is referenced by exactly one owner (the FK is unique on each
/// owner table), so it is effectively one-account-per-owner.
/// </summary>
public sealed class BankAccount : BaseAuditableEntity<Guid>, IAggregateRoot
{
    public Guid BankId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Iban { get; private set; } = string.Empty;

    private BankAccount() { }

    public BankAccount(Guid id, Guid bankId, string name, string iban)
    {
        Id = id;
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
