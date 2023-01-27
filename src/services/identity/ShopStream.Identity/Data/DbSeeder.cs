using Microsoft.EntityFrameworkCore;
using ShopStream.Contracts;
using ShopStream.Identity.Auth;
using ShopStream.Identity.Domain;

namespace ShopStream.Identity.Data;

public static class DbSeeder
{
    public const string DemoPasswordSetting = "SEED_DEMO_PASSWORD";

    // fixed ids so orders/payments can seed Jane's history
    private static readonly (Guid Id, string Email, string Name, string Role)[] DemoUsers =
    {
        (DemoAccounts.AdminId, DemoAccounts.AdminEmail, "Avery Admin", Roles.Admin),
        (DemoAccounts.CustomerId, DemoAccounts.CustomerEmail, "Casey Customer", Roles.Customer),
        (DemoAccounts.JaneId, DemoAccounts.JaneEmail, "Jane Doe", Roles.Customer)
    };

    public static async Task MigrateAndSeedAsync(IServiceProvider services, ILogger logger, bool seedDemoData)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        if (db.Database.IsRelational())
        {
            await db.Database.MigrateAsync();
        }
        else
        {
            await db.Database.EnsureCreatedAsync();
        }

        if (!seedDemoData)
        {
            return;
        }

        var password = config[DemoPasswordSetting];
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("{Setting} is not set; skipping the demo accounts", DemoPasswordSetting);
            return;
        }

        foreach (var (id, email, name, role) in DemoUsers)
        {
            if (await db.Users.AnyAsync(u => u.Email == email))
            {
                continue;
            }

            db.Users.Add(new User(email, name, hasher.Hash(password), role, id));
            logger.LogInformation("Seeded demo user {Email} ({Role})", email, role);
        }

        await db.SaveChangesAsync();
    }
}
