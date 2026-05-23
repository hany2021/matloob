/**
 * Parsed RFC 7807 ProblemDetails response from the .NET backend.
 *
 * The backend also returns a non-standard <c>code</c> extension on
 * every business-rule error (see <c>EstablishmentErrorCodes</c>,
 * <c>OpportunityErrorCodes</c>, <c>OfferErrorCodes</c>,
 * <c>EvaluationErrorCodes</c>). UI logic should key off
 * <c>code</c> rather than the human-readable detail.
 */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status: number;
  detail?: string;
  traceId?: string;
  code?: string;
  /** Raw JSON body in case the caller needs an extension we didn't parse. */
  raw: unknown;
}
