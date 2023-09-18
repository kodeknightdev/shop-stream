using ShopStream.Orders.Application;
using ShopStream.Orders.Domain;

namespace ShopStream.Orders.UnitTests;

public class SalesSummaryTests
{
    [Fact]
    public void Fills_every_day_and_counts_only_confirmed_revenue()
    {
        var day1 = new DateOnly(2023, 9, 1);
        var at = (int d, int h) => new DateTimeOffset(2023, 9, d, h, 0, 0, TimeSpan.Zero);

        var summary = SalesSummary.Build(3, day1, new[]
        {
            (at(1, 9), OrderStatus.Confirmed, 100m),
            (at(1, 15), OrderStatus.Cancelled, 40m),
            (at(3, 10), OrderStatus.Confirmed, 50m),
            (at(3, 11), OrderStatus.Pending, 10m)
        });

        summary.Daily.Select(d => d.Date).Should().Equal(day1, day1.AddDays(1), day1.AddDays(2));
        summary.Daily[0].Should().BeEquivalentTo(new SalesDayDto(day1, 2, 1, 1, 100m));
        summary.Daily[1].Orders.Should().Be(0);
        summary.Revenue.Should().Be(150m);
        summary.TotalOrders.Should().Be(4);
        summary.AverageOrderValue.Should().Be(75m);
    }

    [Fact]
    public void Empty_window_has_zero_average() =>
        SalesSummary.Build(7, new DateOnly(2023, 9, 1), Array.Empty<(DateTimeOffset, OrderStatus, decimal)>())
            .AverageOrderValue.Should().Be(0);
}
