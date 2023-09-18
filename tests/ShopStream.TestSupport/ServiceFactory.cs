using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ShopStream.TestSupport;

// in-memory host: EF InMemory, kafka off, known JWT key
public class ServiceFactory<TProgram, TContext> : WebApplicationFactory<TProgram>
    where TProgram : class
    where TContext : DbContext
{
    private readonly string _databaseName = $"tests-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = TestTokens.SigningKey,
            ["Kafka:Enabled"] = "false",
            ["ConnectionStrings:Redis"] = "",
            ["SEED_DEMO_PASSWORD"] = TestTokens.DemoPassword,
            ["Logging:Format"] = "text",
            ["Serilog:MinimumLevel:Default"] = "Warning"
        }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<TContext>>();
            services.AddDbContext<TContext>(o => o
                .UseInMemoryDatabase(_databaseName)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
            ConfigureTestServices(services);
        });
    }

    protected virtual void ConfigureTestServices(IServiceCollection services) { }

    public async Task WithDbAsync(Func<TContext, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<TContext>());
    }

    public async Task<T> WithDbAsync<T>(Func<TContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TContext>());
    }
}
