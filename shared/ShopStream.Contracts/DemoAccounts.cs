namespace ShopStream.Contracts;

// fixed ids for the Development seeders (identity, orders, payments)
public static class DemoAccounts
{
    public static readonly Guid AdminId = Guid.Parse("5b1f7c2e-6a51-4d1b-9f0e-1c7d2a9e4b01");
    public static readonly Guid CustomerId = Guid.Parse("8e3a4d55-2c1b-4f3a-a6d4-7b9c0e1f2a02");
    public static readonly Guid JaneId = Guid.Parse("c4d2e1f0-9b8a-4c7d-b6e5-3f2a1b0c9d03");

    public const string AdminEmail = "admin@shopstream.local";
    public const string CustomerEmail = "customer@shopstream.local";
    public const string JaneEmail = "jane@shopstream.local";

    public static readonly Guid JaneOrder1 = Guid.Parse("a7e0c1d2-0001-4a1b-8c2d-3e4f5a6b7c01");
    public static readonly Guid JaneOrder2 = Guid.Parse("a7e0c1d2-0002-4a1b-8c2d-3e4f5a6b7c02");
    public static readonly Guid JaneOrder3 = Guid.Parse("a7e0c1d2-0003-4a1b-8c2d-3e4f5a6b7c03");
}
