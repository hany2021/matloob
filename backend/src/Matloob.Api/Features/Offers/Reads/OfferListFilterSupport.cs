using System.Globalization;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Offers;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Offers.Reads;

/// <summary>
/// Applies the offer-list query filters the public frontend sends on the
/// sent/received offer screens (<c>OfferFilter</c>): <c>status[]</c>
/// (the حالة العقد checkboxes), <c>offer_creation_date</c>, and
/// <c>city[]</c>. Mirrors the legacy <c>OfferQueryBuilder</c> scopes
/// (<c>byStatus</c>/<c>byOfferCreationDate</c>/<c>byOpportunityCity</c>).
///
/// <para>
/// The frontend sends <c>status</c> as lowercase wire tokens
/// (<c>rejected</c>, …); our column stores the PascalCase enum, so the
/// tokens are parsed via <see cref="OfferStatusWire.TryParse"/> first.
/// </para>
/// </summary>
internal static class OfferListFilterSupport
{
    public static IQueryable<Offer> ApplyFilters(
        this IQueryable<Offer> query, AppDbContext db, HttpContext ctx)
    {
        // status[] — keep only offers whose status is in the selected set.
        var statusTokens = ReadMulti(ctx, "status");
        if (statusTokens.Count > 0)
        {
            var statuses = new List<OfferStatus>();
            foreach (var token in statusTokens)
            {
                if (OfferStatusWire.TryParse(token, out var s)) statuses.Add(s);
            }
            // All tokens unknown → an impossible filter (return nothing),
            // matching "filter to a status that doesn't exist".
            query = query.Where(o => statuses.Contains(o.Status));
        }

        // offer_creation_date — offers created on that calendar day (UTC).
        var dateRaw = ctx.Request.Query["offer_creation_date"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(dateRaw)
            && DateOnly.TryParse(dateRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            var start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var end = start.AddDays(1);
            query = query.Where(o => o.CreatedAt >= start && o.CreatedAt < end);
        }

        // city[] — offers whose opportunity is in one of the selected cities.
        var cityTokens = ReadMulti(ctx, "city");
        var cityIds = cityTokens
            .Select(t => Guid.TryParse(t, out var g) ? g : (Guid?)null)
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .ToList();
        if (cityIds.Count > 0)
        {
            query = query.Where(o => db.Opportunities
                .Any(opp => opp.Id == o.OpportunityId
                    && opp.CityId != null
                    && cityIds.Contains(opp.CityId.Value)));
        }

        // sender_name — the بحث box on the RECEIVED-offers screen (legacy
        // OfferQueryBuilder::bySenderName): match the sending establishment's
        // name. Case-insensitive contains via ToLower() (EF.Functions.ILike
        // doesn't translate on the InMemory provider the tests use).
        var senderName = ctx.Request.Query["sender_name"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(senderName))
        {
            var n = senderName.Trim().ToLowerInvariant();
            query = query.Where(o => db.Establishments
                .Any(e => e.Id == o.SenderEstablishmentId
                    && e.Name != null
                    && e.Name.ToLower().Contains(n)));
        }

        // applicant_name — the بحث box on the SENT-offers screen (legacy
        // OfferQueryBuilder::byApplicantName): match the applicant's name,
        // whether the applier is a user or an establishment. Resolved through
        // the offer's application (applicant FK is exactly one of the two).
        var applicantName = ctx.Request.Query["applicant_name"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(applicantName))
        {
            var n = applicantName.Trim().ToLowerInvariant();
            query = query.Where(o => db.OpportunityApplications.Any(a =>
                a.Id == o.ApplicationId
                && ((a.ApplicantUserId != null && db.Users.Any(u =>
                        u.IdentityId == a.ApplicantUserId
                        && u.Name != null
                        && u.Name.ToLower().Contains(n)))
                    || (a.ApplicantEstablishmentId != null && db.Establishments.Any(e =>
                        e.Id == a.ApplicantEstablishmentId
                        && e.Name != null
                        && e.Name.ToLower().Contains(n))))));
        }

        return query;
    }

    /// <summary>
    /// Reads a repeated query value sent either as <c>key=a&amp;key=b</c> or
    /// the bracketed <c>key[]=a&amp;key[]=b</c> the axios array serializer
    /// emits. Blank entries are dropped.
    /// </summary>
    private static List<string> ReadMulti(HttpContext ctx, string key)
    {
        var values = ctx.Request.Query[key];
        if (values.Count == 0) values = ctx.Request.Query[key + "[]"];
        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .ToList();
    }
}
