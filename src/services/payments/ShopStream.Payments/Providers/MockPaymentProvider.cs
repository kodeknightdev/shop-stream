namespace ShopStream.Payments.Providers;

// offline stand-in for Stripe, knows the same pm_card_* ids
public sealed class MockPaymentProvider : IPaymentProvider
{
    public const decimal MaxAmount = 5_000m;

    private static readonly Dictionary<string, string> Declines = new(StringComparer.Ordinal)
    {
        ["pm_card_chargeDeclined"] = "Your card was declined.",
        ["pm_card_visa_chargeDeclinedInsufficientFunds"] = "Your card has insufficient funds.",
        ["pm_card_chargeDeclinedExpiredCard"] = "Your card has expired."
    };

    private readonly TimeSpan _latency;

    public MockPaymentProvider(TimeSpan? latency = null) => _latency = latency ?? TimeSpan.FromMilliseconds(300);

    public string Name => "mock";

    public async Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken ct)
    {
        await Task.Delay(_latency, ct);

        if (Declines.TryGetValue(request.PaymentMethod, out var reason))
        {
            return ChargeResult.Failure(reason, $"mock_pi_{request.OrderId:N}");
        }

        if (request.Amount > MaxAmount)
        {
            return ChargeResult.Failure($"Amount exceeds the mock limit of {MaxAmount:0.00}.", $"mock_pi_{request.OrderId:N}");
        }

        return ChargeResult.Success($"mock_pi_{request.OrderId:N}");
    }
}
