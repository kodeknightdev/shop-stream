namespace ShopStream.Orders.Domain;

public sealed class InvalidOrderTransitionException : InvalidOperationException
{
    public InvalidOrderTransitionException(OrderStatus from, OrderStatus to)
        : base($"Cannot move an order from {from} to {to}.")
    {
        From = from;
        To = to;
    }

    public OrderStatus From { get; }
    public OrderStatus To { get; }
}

// Pending -> InventoryReserved -> Paid -> Confirmed, or Cancelled from the first two.
// Repeating a transition that already happened is a no-op.
public sealed class Order
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> Allowed = new()
    {
        [OrderStatus.Pending] = new[] { OrderStatus.InventoryReserved, OrderStatus.Cancelled },
        [OrderStatus.InventoryReserved] = new[] { OrderStatus.Paid, OrderStatus.Cancelled },
        [OrderStatus.Paid] = new[] { OrderStatus.Confirmed },
        [OrderStatus.Confirmed] = Array.Empty<OrderStatus>(),
        [OrderStatus.Cancelled] = Array.Empty<OrderStatus>()
    };

    private readonly List<OrderItem> _items = new();
    private readonly List<OrderStatusChange> _history = new();

    private Order() { }

    // id is only passed in by the demo seeder
    public static Order Place(Guid userId, string email, ShippingAddress address, string paymentMethod, IEnumerable<OrderItem> items,
        string currency = "USD", Guid? id = null)
    {
        var lines = items.ToList();
        if (lines.Count == 0)
        {
            throw new ArgumentException("An order needs at least one item.", nameof(items));
        }

        var order = new Order
        {
            Id = id ?? Guid.NewGuid(),
            Number = GenerateNumber(),
            UserId = userId,
            Email = email,
            ShippingAddress = address,
            PaymentMethod = paymentMethod,
            Currency = currency,
            Status = OrderStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        order.UpdatedAt = order.CreatedAt;
        order._items.AddRange(lines);
        order.Total = lines.Sum(i => i.LineTotal);
        order._history.Add(new OrderStatusChange(order.Id, null, OrderStatus.Pending, "Order placed"));
        return order;
    }

    public Guid Id { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public Guid UserId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public OrderStatus Status { get; private set; }
    public decimal Total { get; private set; }
    public string Currency { get; private set; } = "USD";
    public string PaymentMethod { get; private set; } = string.Empty;
    public ShippingAddress ShippingAddress { get; private set; } = null!;
    public Guid? ReservationId { get; private set; }
    public Guid? PaymentId { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<OrderItem> Items => _items;
    public IReadOnlyList<OrderStatusChange> History => _history;

    public static bool CanTransition(OrderStatus from, OrderStatus to) => Allowed[from].Contains(to);

    public bool CanBeCancelledByCustomer => CanTransition(Status, OrderStatus.Cancelled);

    // false = already done
    public bool MarkInventoryReserved(Guid reservationId)
    {
        if (Status == OrderStatus.InventoryReserved && ReservationId == reservationId)
        {
            return false;
        }

        MoveTo(OrderStatus.InventoryReserved, "Stock reserved");
        ReservationId = reservationId;
        return true;
    }

    public bool MarkPaid(Guid paymentId)
    {
        if (Status is OrderStatus.Paid or OrderStatus.Confirmed && PaymentId == paymentId)
        {
            return false;
        }

        MoveTo(OrderStatus.Paid, "Payment captured");
        PaymentId = paymentId;
        return true;
    }

    public bool Confirm()
    {
        if (Status == OrderStatus.Confirmed)
        {
            return false;
        }

        MoveTo(OrderStatus.Confirmed, "Order confirmed");
        return true;
    }

    public bool Cancel(string reason)
    {
        if (Status == OrderStatus.Cancelled)
        {
            return false;
        }

        MoveTo(OrderStatus.Cancelled, reason);
        CancellationReason = reason;
        return true;
    }

    private void MoveTo(OrderStatus next, string note)
    {
        if (!CanTransition(Status, next))
        {
            throw new InvalidOrderTransitionException(Status, next);
        }

        _history.Add(new OrderStatusChange(Id, Status, next, note));
        Status = next;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string GenerateNumber() =>
        $"SS-{DateTime.UtcNow:yyMMdd}-{Random.Shared.Next(0, 1_000_000):D6}";
}

public sealed class OrderItem
{
    private OrderItem() { }

    public OrderItem(Guid productId, string sku, string name, int quantity, decimal unitPrice, string? imageUrl = null)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        Id = Guid.NewGuid();
        ProductId = productId;
        Sku = sku;
        Name = name;
        Quantity = quantity;
        UnitPrice = unitPrice;
        ImageUrl = imageUrl;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public string? ImageUrl { get; private set; }
    public decimal LineTotal => UnitPrice * Quantity;
}

public sealed class OrderStatusChange
{
    private OrderStatusChange() { }

    public OrderStatusChange(Guid orderId, OrderStatus? from, OrderStatus to, string note)
    {
        Id = Guid.NewGuid();
        OrderId = orderId;
        From = from;
        To = to;
        Note = note;
        OccurredAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public OrderStatus? From { get; private set; }
    public OrderStatus To { get; private set; }
    public string Note { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
}

public sealed record ShippingAddress(string FullName, string Line1, string? Line2, string City, string PostalCode, string Country);
