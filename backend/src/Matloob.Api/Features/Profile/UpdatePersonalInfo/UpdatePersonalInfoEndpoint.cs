using FastEndpoints;
using Matloob.Api.Features.Common;
using Matloob.Api.Features.Profile.Common;
using Matloob.Api.Features.Profile.Show;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Profile.UpdatePersonalInfo;

/// <summary>
/// <c>PATCH /api/users/profile/personal-info</c> (+ canonical
/// <c>/api/v1/users/profile/personal-info</c>) — update the current user's
/// personal-info section and their bank account (upsert).
///
/// Mirrors the Laravel <c>UpdatePersonalInfoController</c>: updates the user
/// scalar fields + city/region, upserts the single <c>bank_accounts</c> row,
/// recomputes profile completion, and returns the refreshed
/// <c>UserResource</c> wrapped in a <c>{ data }</c> envelope.
///
/// Accepts POST and PATCH (the frontend POSTs with a spoofed
/// <c>_method=patch</c>). Precognition pre-validation requests stop after
/// validation with 204.
///
/// Auth: <see cref="MatloobPolicies.User"/>.
/// </summary>
public sealed class UpdatePersonalInfoEndpoint
    : Endpoint<UpdatePersonalInfoRequest, DataEnvelope<ProfileResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public UpdatePersonalInfoEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Verbs(Http.POST, Http.PATCH);
        Routes(
            "/api/users/profile/personal-info",
            "/api/v1/users/profile/personal-info");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<DataEnvelope<ProfileResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithTags("Profile"));
        Summary(s => s.Summary = "Update the current user's personal info + bank account.");
    }

    public override async Task HandleAsync(UpdatePersonalInfoRequest req, CancellationToken ct)
    {
        // Precognition pre-validation: validator already ran; stop before mutating.
        if (ProfileMutationSupport.IsPrecognitive(HttpContext))
        {
            await Send.NoContentAsync(ct);
            return;
        }

        var sub = _currentUser.UserId;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.IdentityId == sub, ct);
        if (user is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        // Reference existence + IBAN checks (Laravel returned 422 for these).
        if (!await _db.Cities.AnyAsync(c => c.Id == req.CityId, ct))
            AddError(r => r.CityId, "City not found.");
        if (!await _db.Regions.AnyAsync(r => r.Id == req.RegionId, ct))
            AddError(r => r.RegionId, "Region not found.");
        if (!await _db.Banks.AnyAsync(b => b.Id == req.BankId, ct))
            AddError(r => r.BankId, "Bank not found.");
        if (!IbanValidator.IsValid(req.Iban))
            AddError(r => r.Iban, "IBAN is not valid.");

        if (ValidationFailures.Count > 0)
        {
            await Send.ErrorsAsync(StatusCodes.Status422UnprocessableEntity, ct);
            return;
        }

        user.UpdatePersonalInfo(
            name: req.Name!,
            email: req.Email!,
            phone: req.PhoneNumber!,
            additionalPhone: req.AdditionalPhoneNumber,
            bio: req.Bio,
            cityId: req.CityId,
            regionId: req.RegionId);

        var bank = await _db.BankAccounts.FirstOrDefaultAsync(b => b.UserId == user.Id, ct);
        if (bank is null)
        {
            _db.BankAccounts.Add(new BankAccount(
                Guid.NewGuid(), user.Id, req.BankId!.Value, req.Name!, req.Iban!));
        }
        else
        {
            bank.Update(req.BankId!.Value, req.Name!, req.Iban!);
        }

        await _db.SaveChangesAsync(ct);
        await ProfileMutationSupport.RecomputeProfileCompletedAsync(_db, user, ct);
        await _db.SaveChangesAsync(ct);

        var response = await ProfileReadMapper.BuildAsync(_db, user, ct);
        await Send.OkAsync(new DataEnvelope<ProfileResponse>(response), ct);
    }
}
