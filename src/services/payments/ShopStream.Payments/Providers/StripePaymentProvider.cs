using Stripe;

namespace ShopStream.Payments.Providers;

// test mode only - live keys are refused at startup
public sealed class StripePaymentProvider : IPaymentProvider
{
    private readonly IStripeClient _client;
    private readonly ILogger<StripePaymentProvider> _logger;

    public StripePaymentProvider(IStripeClient client, ILogger<StripePaymentProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    public string Name => "stripe";

    public async Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken ct)
    {
        var service = new PaymentIntentService(_client);
        var options = new PaymentIntentCreateOptions
        {
            Amount = (long)Math.Round(request.Amount * 100m, MidpointRounding.AwayFromZero),
            Currency = request.Currency.ToLowerInvariant(),
            PaymentMethod = request.PaymentMethod,
            PaymentMethodTypes = new List<string> { "card" },
            Confirm = true,
            ReceiptEmail = request.Email,
            Description = $"shop-stream order {request.OrderId}",
            Metadata = new Dictionary<string, string> { ["orderId"] = request.OrderId.ToString() }
        };

        try
        {
            var intent = await service.CreateAsync(options, new RequestOptions { IdempotencyKey = request.IdempotencyKey }, ct);
            return intent.Status == "succeeded"
                ? ChargeResult.Success(intent.Id)
                : ChargeResult.Failure($"PaymentIntent ended in status '{intent.Status}'.", intent.Id);
        }
        catch (StripeException ex) when (ex.StripeError?.Type is "card_error" or "invalid_request_error")
        {
            _logger.LogInformation("Stripe declined order {OrderId}: {Code} {Message}", request.OrderId, ex.StripeError.Code, ex.StripeError.Message);
            return ChargeResult.Failure(ex.StripeError.Message ?? "Payment was declined.", ex.StripeError.PaymentIntent?.Id);
        }
    }
}

public static class PaymentProviderRegistration
{
    public static IServiceCollection AddPaymentProvider(this IServiceCollection services, IConfiguration configuration, ILogger logger)
    {
        var key = configuration.GetSection(StripeOptions.SectionName).Get<StripeOptions>()?.SecretKey?.Trim();

        if (string.IsNullOrEmpty(key) || key.Contains("xxxx", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("No Stripe test key configured - using the mock payment provider");
            services.AddSingleton<IPaymentProvider>(_ => new MockPaymentProvider());
            return services;
        }

        if (!key.StartsWith("sk_test_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only Stripe test-mode secret keys (sk_test_...) are allowed in shop-stream.");
        }

        logger.LogInformation("Using Stripe in test mode");
        services.AddSingleton<IStripeClient>(_ => new StripeClient(key));
        services.AddSingleton<IPaymentProvider, StripePaymentProvider>();
        return services;
    }
}
