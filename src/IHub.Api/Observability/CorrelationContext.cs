namespace IHub.Api.Observability;

public sealed class CorrelationContext
{
    public string CorrelationId { get; internal set; } = "none";
}
