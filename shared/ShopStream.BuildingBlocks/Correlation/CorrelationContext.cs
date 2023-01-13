namespace ShopStream.BuildingBlocks.Correlation;

public static class CorrelationContext
{
    private static readonly AsyncLocal<string?> Current = new();

    public static string? CorrelationId
    {
        get => Current.Value;
        set => Current.Value = value;
    }

    public static string GetOrCreate() => Current.Value ??= Guid.NewGuid().ToString("N");
}
