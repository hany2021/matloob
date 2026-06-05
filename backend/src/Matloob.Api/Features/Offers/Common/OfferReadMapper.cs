using Matloob.Api.Features.Applications.Common;
using Matloob.Api.Features.Opportunities.Common;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Applications;
using Matloob.Domain.Establishments;
using Matloob.Domain.Offers;
using Matloob.Domain.Opportunities;
using Matloob.Domain.Reference;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Offers.Common;

/// <summary>
/// Builds <see cref="OfferResponse"/> rows from EF entities. Centralised
/// so every read + write endpoint returns the same Laravel-compatible
/// shape minus the dropped Ajeer/contract/invoice fields.
/// </summary>
internal static class OfferReadMapper
{
    public static async Task<OfferResponse> MapAsync(
        AppDbContext db,
        Offer offer,
        DateTimeOffset now,
        CancellationToken ct)
    {
        Establishment? sender = await db.Establishments
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == offer.SenderEstablishmentId, ct);

        OpportunityApplication? application = await db.OpportunityApplications
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == offer.ApplicationId, ct);

        Opportunity? opportunity = await db.Opportunities
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == offer.OpportunityId, ct);

        OpportunityResponse? opportunityResponse = null;
        if (opportunity is not null)
        {
            var bundle = await OpportunityReadQueries.LoadSidecarAsync(
                db, opportunity, subClaim: null, establishmentApplicantId: null, ct);
            // `bundle.Event` is the hydrated owning event; the offer detail
            // pages dereference `opportunity.event.name` unguarded.
            opportunityResponse = OpportunityReadMapper.Map(
                opportunity, bundle.Category, bundle.Issuer, bundle.Nationality,
                bundle.SuccessCriteria, bundle.Uploads, bundle.ApplicantsCount,
                bundle.IsApplied, bundle.Event);
        }

        OpportunityApplicationResponse? applicantResponse = null;
        if (application is not null)
        {
            applicantResponse = await ApplicationReadMapper.MapAsync(
                db, application, opportunityResponse, ct);
        }

        // Ajeer job_title (legacy; never set now that Ajeer is dropped).
        JobTitle? jobTitle = null;
        if (offer.JobTitleId is { } jtId)
        {
            jobTitle = await db.JobTitles
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jtId, ct);
        }

        // Matloob profession = opportunity category. The frontend reads
        // `offer.job_title.title`, so project the category under job_title.
        OpportunityCategory? jobTitleCategory = null;
        if (offer.JobTitleCategoryId is { } jtcId)
        {
            jobTitleCategory = await db.OpportunityCategories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == jtcId, ct);
        }

        OfferCancellationRequest? cancellation = await db.OfferCancellationRequests
            .AsNoTracking()
            .Where(c => c.OfferId == offer.Id)
            .OrderByDescending(c => c.RequestedAt)
            .FirstOrDefaultAsync(ct);

        var sentBy = await LoadUserDtoAsync(db, offer.SentByUserId, ct);
        var appliedBy = await LoadUserDtoAsync(db, offer.AppliedByUserId, ct);

        var expired = offer.OfferValidityTo is { } to
            && to < now
            && IsNonTerminal(offer.Status);

        return new OfferResponse
        {
            Id = offer.Id,
            Sender = sender is null
                ? null
                : new OfferSenderDto
                {
                    Id = sender.Id,
                    Name = sender.Name,
                    Email = sender.Email,
                },
            Applicant = applicantResponse,
            Opportunity = opportunityResponse,
            JobTitle = jobTitleCategory is not null
                ? new OfferJobTitleDto
                {
                    Id = jobTitleCategory.Id,
                    Title = jobTitleCategory.Title,
                    Name = jobTitleCategory.Title,
                }
                : jobTitle is null
                    ? null
                    : new OfferJobTitleDto
                    {
                        Id = jobTitle.Id,
                        Name = jobTitle.Name,
                    },
            MonthlySalary = offer.MonthlySalary,
            DailyWage = offer.DailyWage,
            NumberOfWorkingDays = offer.NumberOfWorkingDays,
            Currency = offer.Currency.ToString(),
            Status = offer.Status.ToWire(),
            StatusColor = null,
            StatusLabel = null,
            OfferValidityFrom = offer.OfferValidityFrom?.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            OfferValidityTo = offer.OfferValidityTo?.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            ExpiryDate = offer.OfferValidityTo?.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            StartDate = offer.StartDate?.ToString("yyyy-MM-dd"),
            EndDate = offer.EndDate?.ToString("yyyy-MM-dd"),
            OtherDetails = offer.OtherDetails,
            LaborerCommitments = offer.LaborerCommitments,
            CreatedAt = offer.CreatedAt.ToString("yyyy-MM-dd"),
            Expired = expired,
            Evaluated = false, // overlap with evaluations — true after both sides post.
            CancelledBy = cancellation is null
                ? null
                : cancellation.RequestedByUserId is not null ? "user" : "organization",
            CancellationRequest = cancellation is null ? null : new OfferCancellationRequestDto
            {
                Id = cancellation.Id,
                RequestedByType = cancellation.RequestedByUserId is not null ? "user" : "organization",
                RequestedById = cancellation.RequestedByUserId
                    ?? cancellation.RequestedByEstablishmentId?.ToString() ?? string.Empty,
                ReasonId = cancellation.OfferCancellationReasonId,
                OtherReason = cancellation.OtherReason,
                IsApproved = cancellation.IsApproved,
                IsRejected = cancellation.IsRejected,
                RequestedAt = cancellation.RequestedAt,
                ReviewedAt = cancellation.ReviewedAt,
            },
            AppliedBy = appliedBy,
            SentBy = sentBy,
            IsPendingSponsorApproval = offer.Status == OfferStatus.PendingSponsorApproval,
            AcceptedAt = offer.AcceptedAt,
        };
    }

    private static async Task<ApplicationAppliedByDto?> LoadUserDtoAsync(
        AppDbContext db,
        string? sub,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sub)) return null;
        var u = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdentityId == sub, ct);
        return new ApplicationAppliedByDto
        {
            Id = sub,
            Name = u?.Name,
            Email = u?.Email,
        };
    }

    private static bool IsNonTerminal(OfferStatus status) => status is
        OfferStatus.Pending or
        OfferStatus.PendingSponsorApproval;
}
