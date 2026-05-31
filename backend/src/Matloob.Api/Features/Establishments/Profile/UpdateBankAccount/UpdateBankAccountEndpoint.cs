using FastEndpoints;
using Matloob.Api.Features.Common;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Profile.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Profile.UpdateBankAccount;

/// <summary>
/// <c>PATCH /api/establishments/me/profile/bank-account</c> (+ canonical
/// <c>/api/v1/establishments/me/profile/bank-account</c>) — upsert the resolved
/// establishment's bank account.
///
/// Mirrors Laravel <c>UpdateProfileBankAccountController</c>: validates the bank
/// exists and the IBAN is well-formed (422), then updateOrCreate's the single
/// account. The account is the shared, ownerless <see cref="BankAccount"/>
/// aggregate, linked via the establishment's owner-side FK. JSON +
/// laravel-precognition (validation-only requests stop with 204).
///
/// Auth: active member (or admin); writes blocked with 423 while Suspended.
/// </summary>
public sealed class UpdateBankAccountEndpoint
    : Endpoint<UpdateBankAccountRequest, DataEnvelope<EstablishmentMeProfileResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public UpdateBankAccountEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Verbs(Http.POST, Http.PATCH);
        Routes(
            "/api/establishments/me/profile/bank-account",
            "/api/v1/establishments/me/profile/bank-account");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<DataEnvelope<EstablishmentMeProfileResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Establishment Profile"));
        Summary(s => s.Summary = "Upsert the resolved establishment's bank account.");
    }

    public override async Task HandleAsync(UpdateBankAccountRequest req, CancellationToken ct)
    {
        if (HttpContext.Request.Headers.ContainsKey("Precognition"))
        {
            await Send.NoContentAsync(ct);
            return;
        }

        var establishmentId = await EstablishmentResourceGuards
            .ResolveForWriteAsync(_db, HttpContext, _currentUser.UserId, ct);
        if (establishmentId is null) return;

        // Reference + IBAN checks (Laravel returned 422 for these).
        if (!await _db.Banks.AnyAsync(b => b.Id == req.BankId, ct))
            AddError(r => r.BankId, "Bank not found.");
        if (!IbanValidator.IsValid(req.Iban))
            AddError(r => r.Iban, "IBAN is not valid.");

        if (ValidationFailures.Count > 0)
        {
            await Send.ErrorsAsync(StatusCodes.Status422UnprocessableEntity, ct);
            return;
        }

        var establishment = await _db.Establishments
            .FirstOrDefaultAsync(e => e.Id == establishmentId.Value, ct);
        if (establishment is null) { await Send.NotFoundAsync(ct); return; }

        var account = establishment.BankAccountId is null
            ? null
            : await _db.BankAccounts.FirstOrDefaultAsync(b => b.Id == establishment.BankAccountId, ct);
        if (account is null)
        {
            account = new BankAccount(Guid.NewGuid(), req.BankId!.Value, req.Name!, req.Iban!);
            _db.BankAccounts.Add(account);
            establishment.SetBankAccount(account.Id);
        }
        else
        {
            account.Update(req.BankId!.Value, req.Name!, req.Iban!);
        }

        await _db.SaveChangesAsync(ct);

        var response = await EstablishmentProfileReadMapper.BuildAsync(_db, establishment, ct);
        await Send.OkAsync(new DataEnvelope<EstablishmentMeProfileResponse>(response), ct);
    }
}
