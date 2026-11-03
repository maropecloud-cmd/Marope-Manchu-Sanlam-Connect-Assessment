namespace IHub.Api.Features.Statements;

/// <param name="Totals">One entry per currency; amounts are never converted.</param>
/// <param name="LastUpdatedAt">When any activity in the window last changed; <c>null</c> if there are none.</param>
/// <param name="GeneratedAt">When the statement was computed.</param>
public sealed record StatementResponse(
    string IntermediaryId,
    StatementWindow Window,
    IReadOnlyList<CurrencyTotal> Totals,
    DateTimeOffset? LastUpdatedAt,
    DateTimeOffset GeneratedAt);

/// <param name="From">Inclusive, UTC.</param>
/// <param name="To">Exclusive, UTC.</param>
public sealed record StatementWindow(DateTimeOffset From, DateTimeOffset To);

/// <param name="GrossAmount">Sum of active activities.</param>
/// <param name="ReversedActivityCount">Reversed activities, excluded from the total.</param>
public sealed record CurrencyTotal(string Currency, decimal GrossAmount, int ActivityCount, int ReversedActivityCount);
