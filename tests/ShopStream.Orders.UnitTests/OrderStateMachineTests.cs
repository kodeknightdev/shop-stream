using ShopStream.Orders.Domain;

namespace ShopStream.Orders.UnitTests;

public class OrderStateMachineTests
{
    private static Order NewOrder() => Order.Place(
        Guid.NewGuid(), "casey@example.com",
        new ShippingAddress("Casey Customer", "1 Main St", null, "Springfield", "12345", "US"),
        "pm_card_visa",
        new[] { new OrderItem(Guid.NewGuid(), "ELE-1001", "Headphones", 2, 50m), new OrderItem(Guid.NewGuid(), "BOO-1002", "Book", 1, 15.5m) });

    [Fact]
    public void Place_starts_pending_with_total_and_history()
    {
        var order = NewOrder();

        order.Status.Should().Be(OrderStatus.Pending);
        order.Total.Should().Be(115.5m);
        order.Number.Should().MatchRegex(@"^SS-\d{6}-\d{6}$");
        order.History.Should().ContainSingle().Which.To.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public void Place_requires_items()
    {
        var act = () => Order.Place(Guid.NewGuid(), "a@b.c", new ShippingAddress("A", "B", null, "C", "D", "US"), "pm_card_visa", Array.Empty<OrderItem>());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Happy_path_walks_every_state_in_order()
    {
        var order = NewOrder();
        var reservation = Guid.NewGuid();
        var payment = Guid.NewGuid();

        order.MarkInventoryReserved(reservation).Should().BeTrue();
        order.MarkPaid(payment).Should().BeTrue();
        order.Confirm().Should().BeTrue();

        order.Status.Should().Be(OrderStatus.Confirmed);
        order.ReservationId.Should().Be(reservation);
        order.PaymentId.Should().Be(payment);
        order.History.Select(h => h.To).Should().Equal(
            OrderStatus.Pending, OrderStatus.InventoryReserved, OrderStatus.Paid, OrderStatus.Confirmed);
        order.CanBeCancelledByCustomer.Should().BeFalse();
    }

    [Fact]
    public void Replaying_a_transition_is_a_no_op()
    {
        var order = NewOrder();
        var reservation = Guid.NewGuid();
        order.MarkInventoryReserved(reservation);

        order.MarkInventoryReserved(reservation).Should().BeFalse();
        order.History.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.InventoryReserved)]
    public void Can_cancel_before_payment(OrderStatus state)
    {
        var order = NewOrder();
        if (state == OrderStatus.InventoryReserved)
        {
            order.MarkInventoryReserved(Guid.NewGuid());
        }

        order.Cancel("Payment failed: card declined").Should().BeTrue();

        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().Be("Payment failed: card declined");
        order.Cancel("again").Should().BeFalse();
    }

    [Fact]
    public void Cannot_pay_before_stock_is_reserved()
    {
        var order = NewOrder();
        var act = () => order.MarkPaid(Guid.NewGuid());
        act.Should().Throw<InvalidOrderTransitionException>().Which.From.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public void Cannot_cancel_after_payment()
    {
        var order = NewOrder();
        order.MarkInventoryReserved(Guid.NewGuid());
        order.MarkPaid(Guid.NewGuid());

        var act = () => order.Cancel("too late");
        act.Should().Throw<InvalidOrderTransitionException>();
    }

    [Fact]
    public void Cancelled_is_terminal()
    {
        var order = NewOrder();
        order.Cancel("Out of stock");

        order.Invoking(o => o.MarkInventoryReserved(Guid.NewGuid())).Should().Throw<InvalidOrderTransitionException>();
        order.Invoking(o => o.Confirm()).Should().Throw<InvalidOrderTransitionException>();
        order.Status.IsTerminal().Should().BeTrue();
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.InventoryReserved, true)]
    [InlineData(OrderStatus.Pending, OrderStatus.Paid, false)]
    [InlineData(OrderStatus.Pending, OrderStatus.Confirmed, false)]
    [InlineData(OrderStatus.InventoryReserved, OrderStatus.Paid, true)]
    [InlineData(OrderStatus.InventoryReserved, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Paid, OrderStatus.Confirmed, true)]
    [InlineData(OrderStatus.Paid, OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Pending, false)]
    public void Transition_table(OrderStatus from, OrderStatus to, bool allowed) =>
        Order.CanTransition(from, to).Should().Be(allowed);
}
