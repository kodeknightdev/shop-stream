using FluentValidation;
using ShopStream.Orders.Domain;

namespace ShopStream.Orders.Application;

public sealed record ShippingAddressDto(string FullName, string Line1, string? Line2, string City, string PostalCode, string Country);

public sealed record PlaceOrderRequest(ShippingAddressDto ShippingAddress, string PaymentMethod);

public sealed record OrderItemDto(Guid ProductId, string Sku, string Name, int Quantity, decimal UnitPrice, decimal LineTotal, string? ImageUrl);

public sealed record StatusChangeDto(string? From, string To, string Note, DateTimeOffset OccurredAt);

public sealed record PriceAdjustmentDto(Guid ProductId, string Name, decimal CartPrice, decimal CatalogPrice);

public sealed record OrderSummaryDto(Guid Id, string Number, string Status, decimal Total, string Currency, int ItemCount, DateTimeOffset CreatedAt, string Email);

public sealed record OrderDetailDto(
    Guid Id,
    string Number,
    string Status,
    decimal Total,
    string Currency,
    string Email,
    string PaymentMethod,
    ShippingAddressDto ShippingAddress,
    IReadOnlyList<OrderItemDto> Items,
    IReadOnlyList<StatusChangeDto> Timeline,
    string? CancellationReason,
    bool CanCancel,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PriceAdjustmentDto>? PriceAdjustments = null);

public sealed record SalesDayDto(DateOnly Date, int Orders, int ConfirmedOrders, int CancelledOrders, decimal Revenue);

public sealed record SalesSummaryDto(int Days, int TotalOrders, int ConfirmedOrders, decimal Revenue, decimal AverageOrderValue, IReadOnlyList<SalesDayDto> Daily);

public static class OrderMapping
{
    public static OrderSummaryDto ToSummary(this Order o) =>
        new(o.Id, o.Number, o.Status.ToString(), o.Total, o.Currency, o.Items.Sum(i => i.Quantity), o.CreatedAt, o.Email);

    public static OrderDetailDto ToDetail(this Order o, IReadOnlyList<PriceAdjustmentDto>? adjustments = null) => new(
        o.Id, o.Number, o.Status.ToString(), o.Total, o.Currency, o.Email, o.PaymentMethod,
        new ShippingAddressDto(o.ShippingAddress.FullName, o.ShippingAddress.Line1, o.ShippingAddress.Line2, o.ShippingAddress.City,
            o.ShippingAddress.PostalCode, o.ShippingAddress.Country),
        o.Items.Select(i => new OrderItemDto(i.ProductId, i.Sku, i.Name, i.Quantity, i.UnitPrice, i.LineTotal, i.ImageUrl)).ToList(),
        o.History.OrderBy(h => h.OccurredAt).Select(h => new StatusChangeDto(h.From?.ToString(), h.To.ToString(), h.Note, h.OccurredAt)).ToList(),
        o.CancellationReason, o.CanBeCancelledByCustomer, o.CreatedAt, o.UpdatedAt, adjustments);
}

public sealed class PlaceOrderRequestValidator : AbstractValidator<PlaceOrderRequest>
{
    public PlaceOrderRequestValidator()
    {
        RuleFor(x => x.PaymentMethod).NotEmpty().MaximumLength(100).Matches("^pm_[A-Za-z0-9_]+$")
            .WithMessage("PaymentMethod must be a Stripe payment method id such as pm_card_visa.");
        RuleFor(x => x.ShippingAddress).NotNull();
        RuleFor(x => x.ShippingAddress.FullName).NotEmpty().MaximumLength(200).When(x => x.ShippingAddress is not null);
        RuleFor(x => x.ShippingAddress.Line1).NotEmpty().MaximumLength(200).When(x => x.ShippingAddress is not null);
        RuleFor(x => x.ShippingAddress.Line2).MaximumLength(200).When(x => x.ShippingAddress is not null);
        RuleFor(x => x.ShippingAddress.City).NotEmpty().MaximumLength(100).When(x => x.ShippingAddress is not null);
        RuleFor(x => x.ShippingAddress.PostalCode).NotEmpty().MaximumLength(20).When(x => x.ShippingAddress is not null);
        RuleFor(x => x.ShippingAddress.Country).NotEmpty().Length(2).When(x => x.ShippingAddress is not null)
            .WithMessage("Country must be a 2-letter ISO code.");
    }
}
