using Microsoft.EntityFrameworkCore;
using ShopStream.Contracts;
using ShopStream.Payments.Domain;

namespace ShopStream.Payments.Data;

// Development only: payments for Jane's seeded orders
public static class DemoPaymentSeeder
{
    public static async Task SeedAsync(PaymentsDbContext db, ILogger logger)
    {
        if (await db.Payments.AnyAsync(p => p.UserId == DemoAccounts.JaneId))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        Add(db, DemoAccounts.JaneOrder1, 163.97m, now.AddDays(-21), failure: null);
        Add(db, DemoAccounts.JaneOrder2, 100.00m, now.AddDays(-9), failure: null);
        Add(db, DemoAccounts.JaneOrder3, 189.00m, now.AddDays(-3), failure: "Your card was declined.");
        await db.SaveChangesAsync();
        logger.LogInformation("Seeded demo payments for {Email}", DemoAccounts.JaneEmail);
    }

    private static void Add(PaymentsDbContext db, Guid orderId, decimal amount, DateTimeOffset at, string? failure)
    {
        var payment = new Payment(orderId, DemoAccounts.JaneId, DemoAccounts.JaneEmail, amount, "USD", "mock", orderId.ToString());
        var reference = $"mock_pi_{orderId:N}";
        if (failure is null)
        {
            payment.Succeed(reference);
        }
        else
        {
            payment.Fail(failure, reference);
        }

        db.Payments.Add(payment);
        db.Entry(payment).Property(p => p.CreatedAt).CurrentValue = at.AddSeconds(2);
    }
}
