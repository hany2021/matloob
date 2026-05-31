namespace Matloob.Api.Features.Common;

/// <summary>
/// Marker for response DTOs that already carry their final wire shape and must
/// NOT be re-wrapped by the global <c>{ data }</c> envelope shim
/// (<see cref="ResponseEnvelopeShim"/>). Two cases:
/// <list type="bullet">
///   <item>The DTO is already a <c>{ data }</c> / <c>{ data, meta, links }</c>
///   envelope — <see cref="DataEnvelope{T}"/>, <see cref="PaginationEnvelope"/>,
///   the notifications <c>mark-as-read</c> response.</item>
///   <item>The DTO is intentionally bare ad-hoc JSON the frontend reads
///   directly — e.g. the notifications <c>unread-count</c> <c>{ count }</c>.</item>
/// </list>
/// </summary>
public interface IBypassEnvelope;
