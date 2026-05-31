using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;

namespace Matloob.Api.Features.Establishments.Services;

/// <summary>
/// Create/update body for an establishment service. Mirrors the legacy Laravel
/// Store/UpdateServiceRequest (name max 40, description max 250, both required).
/// </summary>
public sealed class ServiceUpsertRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

public sealed class ServiceUpsertValidator : Validator<ServiceUpsertRequest>
{
    public ServiceUpsertValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(40);
        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required.")
            .MaximumLength(250);
    }
}
