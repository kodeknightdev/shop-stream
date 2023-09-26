using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ShopStream.Contracts;
using ShopStream.Payments.Application;
using ShopStream.Payments.Data;
using ShopStream.Payments.Domain;
using ShopStream.Payments.Providers;

namespace ShopStream.Payments.Tests;

public sealed class CountingProvider : IPaymentProvider
{
    private readonly MockPaymentProvider _inner = new(TimeSpan.Zero);
    public int Calls { get; private set; }
    public string Name => "mock";

    public Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken ct)
    {
        Calls++;
        return _inner.ChargeAsync(request, ct);
    }
}

public class PaymentProcessorTests
{
    private readonly PaymentsDbContext _db = new(new DbContextOptionsBuilder<PaymentsDbContext>().UseInMemoryDatabase($"pay-{Guid.NewGuid():N}").Options);
    private readonly CountingProvider _provider = new();

    private PaymentProcessor Processor => new(_db, _provider, NullLogger<PaymentProcessor>.Instance);

    private List<(string Topic, JsonElement Payload)> Outbox() => _db.OutboxMessages.AsNoTracking().OrderBy(m => m.CreatedAt).ToList()
        .Select(m => (m.Topic, JsonDocument.Parse(m.Payload).RootElement.GetProperty("payload"))).ToList();

    private static StockReserved Reserved(string paymentMethod = "pm_card_visa", decimal total = 42.5m) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "casey@example.com", total, "USD", paymentMethod, DateTimeOffset.UtcNow.AddMinutes(15));

    [Fact]
    public async Task Successful_charge_is_recorded_with_its_outcome_in_the_outbox()
    {
        var e = Reserved();
        var payment = (await Processor.HandleStockReservedAsync(Guid.NewGuid(), e, default))!;

        payment.Status.Should().Be(PaymentStatus.Succeeded);
        payment.IdempotencyKey.Should().Be(e.OrderId.ToString(), "the order id is the Stripe idempotency key");
        var (topic, body) = Outbox().Should().ContainSingle().Subject;
        topic.Should().Be(Topics.PaymentSucceeded);
        body.GetProperty("amount").GetDecimal().Should().Be(42.5m);
        (await _db.ProcessedEvents.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("pm_card_chargeDeclined", "declined")]
    [InlineData("pm_card_visa_chargeDeclinedInsufficientFunds", "insufficient funds")]
    public async Task Declines_publish_payment_failed(string method, string reasonPart)
    {
        var payment = (await Processor.HandleStockReservedAsync(Guid.NewGuid(), Reserved(method), default))!;

        payment.Status.Should().Be(PaymentStatus.Failed);
        var (topic, body) = Outbox().Single();
        topic.Should().Be(Topics.PaymentFailed);
        body.GetProperty("reason").GetString().Should().Contain(reasonPart);
    }

    [Fact]
    public async Task Mock_rejects_amounts_over_the_limit()
    {
        var payment = (await Processor.HandleStockReservedAsync(Guid.NewGuid(), Reserved(total: MockPaymentProvider.MaxAmount + 1), default))!;
        payment.Status.Should().Be(PaymentStatus.Failed);
    }

    [Fact]
    public async Task Redelivered_event_is_skipped_by_the_inbox()
    {
        var e = Reserved();
        var eventId = Guid.NewGuid();
        await Processor.HandleStockReservedAsync(eventId, e, default);
        var second = await Processor.HandleStockReservedAsync(eventId, e, default);

        second.Should().BeNull();
        _provider.Calls.Should().Be(1);
        Outbox().Should().ContainSingle();
    }

    [Fact]
    public async Task A_new_event_for_an_already_charged_order_never_charges_twice()
    {
        var e = Reserved();
        var first = (await Processor.HandleStockReservedAsync(Guid.NewGuid(), e, default))!;
        var second = (await Processor.HandleStockReservedAsync(Guid.NewGuid(), e, default))!;

        second.Id.Should().Be(first.Id);
        _provider.Calls.Should().Be(1);
        (await _db.Payments.CountAsync()).Should().Be(1);
        Outbox().Should().HaveCount(2).And.OnlyContain(m => m.Topic == Topics.PaymentSucceeded);
    }

    [Fact]
    public void Provider_falls_back_to_mock_without_a_key()
    {
        var services = new ServiceCollection();
        services.AddPaymentProvider(new ConfigurationBuilder().Build(), NullLogger.Instance);
        services.BuildServiceProvider().GetRequiredService<IPaymentProvider>().Name.Should().Be("mock");
    }

    [Fact]
    public void Placeholder_key_from_env_example_also_uses_mock()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Stripe:SecretKey"] = "sk_test_xxxxxxxxxxxxxxxxxxxx" }).Build();
        var services = new ServiceCollection();
        services.AddPaymentProvider(config, NullLogger.Instance);
        services.BuildServiceProvider().GetRequiredService<IPaymentProvider>().Name.Should().Be("mock");
    }

    [Fact]
    public void Test_key_selects_stripe()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Stripe:SecretKey"] = "sk_test_51Example000000000000" }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddPaymentProvider(config, NullLogger.Instance);
        services.BuildServiceProvider().GetRequiredService<IPaymentProvider>().Name.Should().Be("stripe");
    }

    [Fact]
    public void Non_test_keys_are_refused()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Stripe:SecretKey"] = "rk_prod_123" }).Build();
        var act = () => new ServiceCollection().AddPaymentProvider(config, NullLogger.Instance);
        act.Should().Throw<InvalidOperationException>().WithMessage("*test-mode*");
    }
}
