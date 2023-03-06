namespace ShopStream.Orders.Domain;

public enum OrderStatus
{
    Pending,
    InventoryReserved,
    Paid,
    Confirmed,
    Cancelled
}

public static class OrderStatusExtensions
{
    public static bool IsTerminal(this OrderStatus status) => status is OrderStatus.Confirmed or OrderStatus.Cancelled;
}
