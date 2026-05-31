using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;

namespace Matloob.Api.Features.Establishments.Products;

/// <summary>
/// Create/update body for an establishment product. Mirrors the legacy Laravel
/// StoreProductRequest (name max 40, description max 250, both required). The
/// frontend submits this as multipart/form-data (postForm), so the endpoints
/// enable form binding.
/// </summary>
public sealed class ProductUpsertRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

public sealed class ProductUpsertValidator : Validator<ProductUpsertRequest>
{
    public ProductUpsertValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(40);
        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required.")
            .MaximumLength(250);
    }
}
