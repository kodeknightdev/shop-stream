using ShopStream.Orders.Domain;

namespace ShopStream.Orders.Application;

public static class SalesSummary
{
    // revenue = confirmed orders only; empty days are included
    public static SalesSummaryDto Build(int days, DateOnly firstDay, IEnumerable<(DateTimeOffset CreatedAt, OrderStatus Status, decimal Total)> orders)
    {
        var byDay = orders.GroupBy(o => DateOnly.FromDateTime(o.CreatedAt.UtcDateTime)).ToDictionary(g => g.Key, g => g.ToList());

        var daily = Enumerable.Range(0, days).Select(i =>
        {
            var day = firstDay.AddDays(i);
            var list = byDay.GetValueOrDefault(day) ?? new();
            return new SalesDayDto(
                day,
                list.Count,
                list.Count(o => o.Status == OrderStatus.Confirmed),
                list.Count(o => o.Status == OrderStatus.Cancelled),
                list.Where(o => o.Status == OrderStatus.Confirmed).Sum(o => o.Total));
        }).ToList();

        var confirmed = daily.Sum(d => d.ConfirmedOrders);
        var revenue = daily.Sum(d => d.Revenue);
        return new SalesSummaryDto(days, daily.Sum(d => d.Orders), confirmed, revenue, confirmed == 0 ? 0 : Math.Round(revenue / confirmed, 2), daily);
    }
}
