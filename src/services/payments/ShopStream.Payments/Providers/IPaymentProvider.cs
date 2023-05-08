namespace ShopStream.Payments.Providers;

public sealed record ChargeRequest(Guid OrderId, decimal Amount, string Currency, string PaymentMethod, string Email, string IdempotencyKey);

public sealed record ChargeResult(bool Succeeded, string? ProviderReference, string? FailureReason)
{
    public static ChargeResult Success(string reference) => new(true, reference, null);
    public static ChargeResult Failure(string reason, string? reference = null) => new(false, reference, reason);
}

public interface IPaymentProvider
{
    string Name { get; }
    Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken ct);
}

public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    // sk_test_... only; empty = mock provider
    public string? SecretKey { get; set; }
}
