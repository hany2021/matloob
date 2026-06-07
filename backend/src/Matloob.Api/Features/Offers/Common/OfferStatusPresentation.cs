using Matloob.Domain.Offers;

namespace Matloob.Api.Features.Offers.Common;

/// <summary>
/// Presentation (label + dot color) for an <see cref="OfferStatus"/>. The
/// individual's contract list card (OfferCard) renders
/// <c>status_color</c> + <c>status_label</c> straight from the API, so they must
/// be populated or every contract shows a colorless "N/A". Labels are Arabic
/// (the platform's primary locale); colors mirror the frontend status palette.
/// </summary>
internal static class OfferStatusPresentation
{
    public static (string Label, string Color) ForStatus(OfferStatus status) => status switch
    {
        OfferStatus.Pending => ("بانتظار الرد", "#E77A29"),
        OfferStatus.Accepted => ("مقبول", "#3C7D44"),
        OfferStatus.Rejected => ("مرفوض", "#DC2626"),
        OfferStatus.Canceled => ("ملغى", "#6B7280"),
        OfferStatus.CancellationRequested => ("طلب إلغاء", "#E77A29"),
        OfferStatus.PendingSponsorApproval => ("بانتظار موافقة الكفيل", "#E77A29"),
        OfferStatus.PendingSponsorCancellationApproval => ("بانتظار موافقة الكفيل على الإلغاء", "#E77A29"),
        OfferStatus.SponsorRejected => ("رفض الكفيل", "#DC2626"),
        OfferStatus.Expired => ("منتهي الصلاحية", "#6B7280"),
        OfferStatus.WaitingForEvaluation => ("بانتظار التقييم", "#2563EB"),
        OfferStatus.Completed => ("مكتمل", "#3C7D44"),
        _ => ("", "#6B7280"),
    };
}
