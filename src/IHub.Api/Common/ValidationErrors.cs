namespace IHub.Api.Common;

internal sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool IsEmpty => Count == 0;

    public int Count { get; private set; }

    public void Add(string field, string message)
    {
        Count++;
        if (!_errors.TryGetValue(field, out var messages))
        {
            messages = [];
            _errors[field] = messages;
        }

        messages.Add(message);
    }

    public Dictionary<string, string[]> ToDictionary() =>
        _errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
}
