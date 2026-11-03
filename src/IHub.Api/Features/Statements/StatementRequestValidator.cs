using IHub.Api.Common;
using IHub.Api.Domain;
using Microsoft.Extensions.Options;

namespace IHub.Api.Features.Statements;

internal sealed class StatementRequestValidator(IOptions<StatementOptions> options)
{
    private readonly TimeSpan _maxWindow = options.Value.MaxWindow;

    public bool TryValidate(
        string intermediaryId,
        string? from,
        string? to,
        out StatementWindow window,
        out Dictionary<string, string[]> errors)
    {
        var problems = new ValidationErrors();
        window = default!;

        if (intermediaryId.Length > ActivityLimits.IntermediaryIdMaxLength || !Identifiers.IsValidCode(intermediaryId))
        {
            problems.Add("intermediaryId", "Not a valid intermediary identifier.");
        }

        var fromValid = UtcTimestamp.TryParse(from, out var fromUtc);
        if (!fromValid)
        {
            problems.Add("from", $"Required; must be {UtcTimestamp.FormatHint} (URL-encode '+' as %2B).");
        }

        var toValid = UtcTimestamp.TryParse(to, out var toUtc);
        if (!toValid)
        {
            problems.Add("to", $"Required; must be {UtcTimestamp.FormatHint} (URL-encode '+' as %2B).");
        }

        if (fromValid && toValid)
        {
            if (toUtc <= fromUtc)
            {
                problems.Add("to", "Must be later than 'from'. The window is half-open: [from, to).");
            }
            else if (toUtc - fromUtc > _maxWindow)
            {
                problems.Add("to", $"The window may span at most {_maxWindow.TotalDays:0} days.");
            }
        }

        errors = problems.ToDictionary();
        if (!problems.IsEmpty)
        {
            return false;
        }

        window = new StatementWindow(fromUtc, toUtc);
        return true;
    }
}
