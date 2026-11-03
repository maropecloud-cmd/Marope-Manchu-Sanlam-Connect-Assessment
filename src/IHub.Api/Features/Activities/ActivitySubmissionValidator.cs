using IHub.Api.Common;
using IHub.Api.Domain;
using Microsoft.Extensions.Options;

namespace IHub.Api.Features.Activities;

internal sealed class ActivitySubmissionValidator(IOptions<IngestionOptions> options, TimeProvider timeProvider)
{
    private readonly IngestionOptions _options = options.Value;
    private readonly HashSet<string> _currencies =
        new(options.Value.SupportedCurrencies.Select(c => c.ToUpperInvariant()), StringComparer.Ordinal);

    public bool TryValidate(
        SubmitActivitiesRequest request,
        out IReadOnlyList<ActivityRevision> revisions,
        out Dictionary<string, string[]> errors)
    {
        var problems = new ValidationErrors();
        var valid = new List<ActivityRevision>();

        if (request.Activities is not { Count: > 0 } activities)
        {
            problems.Add("activities", "At least one activity is required.");
        }
        else if (activities.Count > _options.MaxBatchSize)
        {
            problems.Add("activities", $"A batch may contain at most {_options.MaxBatchSize} activities.");
        }
        else
        {
            var latestAllowed = timeProvider.GetUtcNow() + _options.MaxClockSkew;
            for (var index = 0; index < activities.Count; index++)
            {
                if (TryConvert(activities[index], $"activities[{index}]", latestAllowed, problems) is { } revision)
                {
                    valid.Add(revision);
                }
            }
        }

        revisions = problems.IsEmpty ? valid : [];
        errors = problems.ToDictionary();
        return problems.IsEmpty;
    }

    private ActivityRevision? TryConvert(
        ActivitySubmission? item, string path, DateTimeOffset latestAllowed, ValidationErrors problems)
    {
        if (item is null)
        {
            problems.Add(path, "Activity must not be null.");
            return null;
        }

        var errorsBefore = problems.Count;

        var sourceSystem = RequireCode(item.SourceSystem, $"{path}.sourceSystem", ActivityLimits.SourceSystemMaxLength, problems);
        var sourceActivityId = RequireExternalId(item.SourceActivityId, $"{path}.sourceActivityId", problems);
        var intermediaryId = RequireCode(item.IntermediaryId, $"{path}.intermediaryId", ActivityLimits.IntermediaryIdMaxLength, problems);
        var activityType = RequireCode(item.ActivityType, $"{path}.activityType", ActivityLimits.ActivityTypeMaxLength, problems);

        if (item.Version is not >= 1)
        {
            problems.Add($"{path}.version", "Version is required and must be 1 or greater.");
        }

        var status = ParseStatus(item.Status);
        if (status is null)
        {
            problems.Add($"{path}.status", "Status is required and must be 'Active' or 'Reversed'.");
        }

        ValidateAmount(item.Amount, $"{path}.amount", problems);

        var currency = item.Currency?.ToUpperInvariant();
        if (currency is null || !_currencies.Contains(currency))
        {
            problems.Add($"{path}.currency", $"Currency is required and must be one of: {string.Join(", ", _currencies.Order())}.");
        }

        if (!UtcTimestamp.TryParse(item.OccurredAt, out var occurredAt))
        {
            problems.Add($"{path}.occurredAt", $"OccurredAt is required and must be {UtcTimestamp.FormatHint}.");
        }
        else if (occurredAt > latestAllowed)
        {
            problems.Add($"{path}.occurredAt", "OccurredAt must not be in the future.");
        }

        if (problems.Count != errorsBefore)
        {
            return null;
        }

        return new ActivityRevision(
            sourceSystem!,
            sourceActivityId!,
            item.Version!.Value,
            intermediaryId!,
            activityType!,
            status!.Value,
            item.Amount!.Value,
            currency!,
            occurredAt);
    }

    private static string? RequireCode(string? value, string field, int maxLength, ValidationErrors problems)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maxLength || !Identifiers.IsValidCode(value))
        {
            problems.Add(field, $"Required; up to {maxLength} characters of letters, digits, '.', '_' or '-', starting with a letter or digit.");
            return null;
        }

        return value;
    }

    private static string? RequireExternalId(string? value, string field, ValidationErrors problems)
    {
        if (string.IsNullOrEmpty(value) || value.Length > ActivityLimits.SourceActivityIdMaxLength || !Identifiers.IsValidExternalId(value))
        {
            problems.Add(field, $"Required; up to {ActivityLimits.SourceActivityIdMaxLength} visible ASCII characters, no spaces.");
            return null;
        }

        return value;
    }

    private static ActivityStatus? ParseStatus(string? value) => value?.ToUpperInvariant() switch
    {
        "ACTIVE" => ActivityStatus.Active,
        "REVERSED" => ActivityStatus.Reversed,
        _ => null,
    };

    private static void ValidateAmount(decimal? amount, string field, ValidationErrors problems)
    {
        if (amount is null)
        {
            problems.Add(field, "Amount is required.");
        }
        else if (amount < 0)
        {
            problems.Add(field, "Amount must not be negative. Cancellations are submitted as a new version with status 'Reversed'.");
        }
        else if (amount > ActivityLimits.MaxAmount)
        {
            problems.Add(field, "Amount is too large.");
        }
        else if (decimal.Round(amount.Value, ActivityLimits.AmountScale) != amount.Value)
        {
            problems.Add(field, $"Amount must have at most {ActivityLimits.AmountScale} decimal places.");
        }
    }
}
