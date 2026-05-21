using FastEndpoints;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;

namespace Matloob.Api.Features.Establishments.Registration.CreateDraft;

/// <summary>
/// <c>POST /api/v1/establishments/registration/drafts</c> — creates an empty
/// <see cref="Establishment"/> row in <c>Status = Draft</c> owned by the
/// authenticated user.
///
/// Auth: <see cref="MatloobPolicies.User"/> — authenticated bearer carrying
/// the <c>matloob_user</c> role. Admins-only callers (no matloob_user role)
/// receive 403; anonymous receives 401.
///
/// No request body in this commit. The §3 fields are filled in by the
/// basic-info PATCH endpoint that lands in a later commit; SubmitForReview
/// gates the lifecycle transition once those fields are populated.
///
/// The system-wide audit interceptor stamps CreatedAt / CreatedBy. The
/// CreatedByUserId column on the row mirrors CreatedBy intentionally —
/// CreatedBy is shared infrastructure for every entity; CreatedByUserId is
/// the domain concept that drives "this user becomes the first Owner on
/// approval" (spec §6.2), and we don't want to depend on the interceptor's
/// scope to know that.
/// </summary>
public sealed class CreateDraftEndpoint : EndpointWithoutRequest<CreateDraftResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public CreateDraftEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Post("/api/v1/establishments/registration/drafts");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<CreateDraftResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithTags("Establishments"));
        Summary(s =>
        {
            s.Summary = "Create a Draft establishment owned by the current user.";
            s.Description =
                "No body required. The §3 fields (name, CR number, etc.) are " +
                "filled in via the basic-info PATCH endpoint. Submitting for " +
                "review is a separate transition.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        // ICurrentUser.UserId resolves to "system" outside an HTTP context;
        // here we know the policy passed, so UserId is the JWT sub claim.
        var establishment = Establishment.CreateDraft(
            id: Guid.NewGuid(),
            createdByUserId: _currentUser.UserId);

        _db.Establishments.Add(establishment);
        await _db.SaveChangesAsync(ct);

        var response = new CreateDraftResponse(
            Id: establishment.Id,
            Status: establishment.Status,
            CreatedAt: establishment.CreatedAt);

        HttpContext.Response.Headers.Location =
            $"/api/v1/establishments/registration/{establishment.Id}";
        await Send.ResponseAsync(response, StatusCodes.Status201Created, ct);
    }
}
