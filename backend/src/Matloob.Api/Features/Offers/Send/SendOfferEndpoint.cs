using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Offers.Common;
using Matloob.Api.Features.Offers.Reads;
using Matloob.Api.Features.Opportunities.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Events;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Offers;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Offers.Send;

/// <summary>
/// <c>POST /api/establishments/offers/send</c> +
/// <c>POST /api/v1/establishments/{establishmentId}/offers/send</c> —
/// the resolved establishment sends an offer to one of the applicants
/// on an opportunity it issued.
///
/// <para>
/// Rules (Laravel <c>SendOfferRequest</c> minus Ajeer):
/// </para>
/// <list type="bullet">
///   <item>Applicant must exist + belong to an opportunity issued by
///     the sender establishment.</item>
///   <item>No offer can already exist for the (applicant) pair.</item>
///   <item>Validity window: <c>offer_validity_from &lt; offer_validity_to</c>.</item>
///   <item>Date window: <c>end_date &gt;= start_date</c>.</item>
///   <item>If <c>sponsor_id</c> is set the offer starts in
///     <see cref="OfferStatus.PendingSponsorApproval"/>; otherwise
///     <see cref="OfferStatus.Pending"/>.</item>
///   <item>Suspended establishment blocked (423).</item>
/// </list>
/// </summary>
public sealed class SendOfferEndpoint
    : Endpoint<SendOfferRequest, OfferResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IOutboxWriter _outbox;

    public SendOfferEndpoint(
        AppDbContext db,
        ICurrentUser currentUser,
        TimeProvider clock,
        IOutboxWriter outbox)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _outbox = outbox;
    }

    public override void Configure()
    {
        Post(
            "/api/establishments/offers/send",
            "/api/v1/establishments/{establishmentId}/offers/send");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<OfferResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Offers"));
        Summary(s => s.Summary = "Send an offer to an applicant on one of the establishment's opportunities.");
    }

    public override async Task HandleAsync(SendOfferRequest req, CancellationToken ct)
    {
        // Precognition validate-only ping — the offer form's step-1 "next"
        // (تفاصيل العرض → مراجعة وإرسال) sends one. FluentValidation has
        // already run; stop before any persistence so the ping never creates
        // a real offer. (Mirrors the profile/event write slices.)
        if (HttpContext.Request.Headers.ContainsKey("Precognition"))
        {
            await Send.NoContentAsync(ct);
            return;
        }

        var sub = _currentUser.UserId;
        var establishmentId = await OpportunityWriteGuards.AuthoriseMutationAsync(
            _db, HttpContext, sub, ct);
        if (establishmentId is null) return;

        var application = await _db.OpportunityApplications
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == req.ApplicantId, ct);
        if (application is null)
        {
            await ProblemWriter.WriteAsync(
                HttpContext,
                StatusCodes.Status422UnprocessableEntity,
                OfferErrorCodes.ApplicantNotFound,
                "Applicant does not exist.",
                ct);
            return;
        }

        var opportunity = await _db.Opportunities
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == application.OpportunityId, ct);
        if (opportunity is null
            || opportunity.IssuerEstablishmentId != establishmentId.Value)
        {
            await ProblemWriter.WriteAsync(
                HttpContext,
                StatusCodes.Status422UnprocessableEntity,
                OfferErrorCodes.ApplicantNotVisible,
                "Applicant does not belong to one of your opportunities.",
                ct);
            return;
        }

        // One offer per applicant.
        var alreadySent = await _db.Offers
            .AsNoTracking()
            .AnyAsync(o => o.ApplicationId == application.Id, ct);
        if (alreadySent)
        {
            await ProblemWriter.WriteAsync(
                HttpContext,
                StatusCodes.Status409Conflict,
                OfferErrorCodes.OfferAlreadyExists,
                "An offer has already been sent for this applicant.",
                ct);
            return;
        }

        // Sponsor existence + eligibility (if set).
        if (req.SponsorId is { } sponsorId)
        {
            var sponsor = await _db.Establishments
                .AsNoTracking()
                .Where(e => e.Id == sponsorId)
                .Select(e => new { e.Id, e.IsSponsor })
                .FirstOrDefaultAsync(ct);
            if (sponsor is null)
            {
                await ProblemWriter.WriteAsync(
                    HttpContext,
                    StatusCodes.Status422UnprocessableEntity,
                    OfferErrorCodes.SponsorNotFound,
                    "Sponsor establishment does not exist.",
                    ct);
                return;
            }
            if (!sponsor.IsSponsor)
            {
                await ProblemWriter.WriteAsync(
                    HttpContext,
                    StatusCodes.Status422UnprocessableEntity,
                    OfferErrorCodes.SponsorNotEligible,
                    "Establishment is not marked as a sponsor.",
                    ct);
                return;
            }
        }

        // Profession: since Ajeer is dropped, the form's "المهنة" select
        // always carries an OPPORTUNITY-CATEGORY id in `job_title_id` (the
        // ajeer→job_titles branch never runs). Store it in its proper FK
        // column (`job_title_category_id` → opportunity_categories), NOT
        // `job_title_id` (→ job_titles), which would FK-violate.
        var professionCategoryId = req.JobTitleCategoryId ?? req.JobTitleId;
        if (professionCategoryId is { } profId
            && !await _db.OpportunityCategories.AsNoTracking().AnyAsync(c => c.Id == profId, ct))
        {
            await ProblemWriter.WriteAsync(
                HttpContext,
                StatusCodes.Status422UnprocessableEntity,
                OfferErrorCodes.ProfessionNotFound,
                "Selected profession (opportunity category) does not exist.",
                ct);
            return;
        }

        var now = _clock.GetUtcNow();
        var startDate = req.StartDate ?? DateOnly.FromDateTime(now.AddDays(31).UtcDateTime);
        var endDate = req.EndDate ?? DateOnly.FromDateTime(now.AddDays(40).UtcDateTime);

        // Salary derivation for a vacancy (daily-wage individual) offer: the
        // form sends only daily_wage, so number_of_working_days = days(start..
        // end) and monthly_salary = daily_wage × days (the period total, stored
        // under monthly_salary). Non-vacancy offers keep the supplied
        // monthly_salary.
        //
        // Day count is INCLUSIVE (+1) to match the frontend wizard
        // (ReviewStep.tsx: `diff(end,start,'days') + 1`) — the contract we
        // protect. Legacy's SendOfferService used an exclusive `diffInDays`,
        // which left the wizard total (6d/900) disagreeing with the stored
        // value (5d/750); aligning to the frontend keeps them consistent.
        var isVacancy = await _db.OpportunityCategories
            .AsNoTracking()
            .Where(c => c.Id == opportunity.OpportunityCategoryId)
            .Select(c => (bool?)c.ForVacancy)
            .FirstOrDefaultAsync(ct) ?? false;

        int? workingDays = req.NumberOfWorkingDays;
        decimal monthlySalary = req.MonthlySalary ?? 0m;
        if (isVacancy)
        {
            workingDays = Math.Abs(endDate.DayNumber - startDate.DayNumber) + 1;
            monthlySalary = (req.DailyWage ?? 0) * (decimal)workingDays.Value;
        }

        Offer offer;
        try
        {
            offer = Offer.Create(
                id: Guid.NewGuid(),
                senderEstablishmentId: establishmentId.Value,
                opportunityId: opportunity.Id,
                applicationId: application.Id,
                sentByUserId: sub,
                // Normalize to UTC: the client sends these with a +03:00
                // (Riyadh) offset, but the column is `timestamptz` and
                // Npgsql only writes offset-0. ToUniversalTime preserves the
                // instant. (`now` is already UTC, so it's a no-op there.)
                offerValidityFrom: (req.OfferValidityFrom ?? now).ToUniversalTime(),
                offerValidityTo: (req.OfferValidityTo ?? now.AddDays(30)).ToUniversalTime(),
                startDate: startDate,
                endDate: endDate,
                monthlySalary: monthlySalary,
                // Ajeer job_titles are dropped — the profession is always an
                // opportunity category, persisted to job_title_category_id.
                jobTitleId: null,
                jobTitleCategoryId: professionCategoryId,
                sponsorEstablishmentId: req.SponsorId,
                appliedByUserId: application.AppliedByUserId,
                dailyWage: req.DailyWage,
                numberOfWorkingDays: workingDays,
                laborerCommitments: req.LaborerCommitments,
                otherDetails: req.OtherDetails);
        }
        catch (ArgumentException ex)
        {
            await ProblemWriter.WriteAsync(
                HttpContext,
                StatusCodes.Status422UnprocessableEntity,
                OfferErrorCodes.InvalidStatusTransition,
                ex.Message,
                ct);
            return;
        }

        _db.Offers.Add(offer);

        _outbox.Enqueue(
            OfferEventTypes.Created,
            aggregateType: nameof(Offer),
            aggregateId: offer.Id,
            payload: new
            {
                id = offer.Id,
                senderEstablishmentId = offer.SenderEstablishmentId,
                opportunityId = offer.OpportunityId,
                applicationId = offer.ApplicationId,
                sponsorEstablishmentId = offer.SponsorEstablishmentId,
                status = offer.Status.ToString(),
                sentAt = now,
                sentByUserId = sub,
            });
        if (offer.Status == OfferStatus.PendingSponsorApproval)
        {
            _outbox.Enqueue(
                OfferEventTypes.SponsorApprovalPending,
                aggregateType: nameof(Offer),
                aggregateId: offer.Id,
                payload: new
                {
                    id = offer.Id,
                    sponsorEstablishmentId = offer.SponsorEstablishmentId,
                });
        }
        _outbox.Flush();

        await _db.SaveChangesAsync(ct);

        var response = await OfferReadMapper.MapAsync(_db, offer, now, ct);
        HttpContext.Response.Headers.Location =
            $"/api/v1/establishments/{establishmentId.Value}/sent-offers/{offer.Id}";
        await Send.ResponseAsync(response, StatusCodes.Status201Created, ct);
    }
}

public sealed class SendOfferRequest
{
    [JsonPropertyName("applicant_id")]
    public Guid ApplicantId { get; init; }

    [JsonPropertyName("job_title_id")]
    public Guid? JobTitleId { get; init; }

    [JsonPropertyName("job_title_category_id")]
    public Guid? JobTitleCategoryId { get; init; }

    [JsonPropertyName("sponsor_id")]
    public Guid? SponsorId { get; init; }

    [JsonPropertyName("daily_wage")]
    public int? DailyWage { get; init; }

    [JsonPropertyName("number_of_working_days")]
    public int? NumberOfWorkingDays { get; init; }

    [JsonPropertyName("monthly_salary")]
    public decimal? MonthlySalary { get; init; }

    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    [JsonPropertyName("offer_validity_from")]
    public DateTimeOffset? OfferValidityFrom { get; init; }

    [JsonPropertyName("offer_validity_to")]
    public DateTimeOffset? OfferValidityTo { get; init; }

    [JsonPropertyName("laborer_commitments")]
    public string? LaborerCommitments { get; init; }

    [JsonPropertyName("start_date")]
    public DateOnly? StartDate { get; init; }

    [JsonPropertyName("end_date")]
    public DateOnly? EndDate { get; init; }

    [JsonPropertyName("other_details")]
    public string? OtherDetails { get; init; }
}

public sealed class SendOfferRequestValidator : Validator<SendOfferRequest>
{
    public SendOfferRequestValidator()
    {
        RuleFor(x => x.ApplicantId).NotEmpty();
        RuleFor(x => x.MonthlySalary).GreaterThanOrEqualTo(0)
            .When(x => x.MonthlySalary.HasValue);
        RuleFor(x => x.DailyWage).GreaterThanOrEqualTo(0)
            .When(x => x.DailyWage.HasValue);
        RuleFor(x => x.NumberOfWorkingDays).GreaterThanOrEqualTo(0)
            .When(x => x.NumberOfWorkingDays.HasValue);
        RuleFor(x => x).Must(x =>
                !(x.OfferValidityFrom.HasValue && x.OfferValidityTo.HasValue)
                || x.OfferValidityTo!.Value > x.OfferValidityFrom!.Value)
            .WithMessage("offer_validity_to must be after offer_validity_from.");
        RuleFor(x => x).Must(x =>
                !(x.StartDate.HasValue && x.EndDate.HasValue)
                || x.EndDate!.Value >= x.StartDate!.Value)
            .WithMessage("end_date must be on or after start_date.");
    }
}
