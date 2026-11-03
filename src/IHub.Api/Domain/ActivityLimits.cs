namespace IHub.Api.Domain;

public static class ActivityLimits
{
    public const int SourceSystemMaxLength = 50;
    public const int SourceActivityIdMaxLength = 100;
    public const int IntermediaryIdMaxLength = 50;
    public const int ActivityTypeMaxLength = 50;
    public const int StatusMaxLength = 16;
    public const int CurrencyLength = 3;
    public const int CorrelationIdMaxLength = 64;
    public const int ContentHashLength = 32;

    public const int AmountPrecision = 19;

    public const int AmountScale = 4;

    public const decimal MaxAmount = 999_999_999_999_999.9999m;
}
