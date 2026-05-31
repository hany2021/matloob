using System.Text.Json.Serialization;

namespace Matloob.Api.Features.Establishments.Profile.UpdateBankAccount;

/// <summary>
/// Body of <c>PATCH /api/establishments/me/profile/bank-account</c>. Snake_case
/// keys mirror the legacy <c>UpdateProfileBankAccountRequest</c>
/// (name + bank_id + iban). JSON via laravel-precognition.
/// </summary>
public sealed class UpdateBankAccountRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("bank_id")]
    public Guid? BankId { get; init; }

    [JsonPropertyName("iban")]
    public string? Iban { get; init; }
}
