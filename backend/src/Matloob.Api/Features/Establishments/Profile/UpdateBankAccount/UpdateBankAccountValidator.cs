using FastEndpoints;
using FluentValidation;

namespace Matloob.Api.Features.Establishments.Profile.UpdateBankAccount;

/// <summary>
/// Shape validation for bank-account edits (mirrors the legacy
/// <c>UpdateProfileBankAccountRequest</c>: name + bank_id + iban required).
/// Bank existence and IBAN checksum are checked in the handler (need the DB /
/// the IBAN algorithm), returning 422 to match Laravel.
/// </summary>
public sealed class UpdateBankAccountValidator : Validator<UpdateBankAccountRequest>
{
    public UpdateBankAccountValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Account holder name is required.")
            .MaximumLength(200);

        RuleFor(x => x.BankId)
            .NotNull().NotEqual(Guid.Empty).WithMessage("Bank is required.");

        RuleFor(x => x.Iban)
            .NotEmpty().WithMessage("IBAN is required.")
            .MaximumLength(34);
    }
}
