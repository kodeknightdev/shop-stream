namespace ShopStream.Contracts;

public static class Roles
{
    public const string Customer = "Customer";
    public const string Admin = "Admin";
}

public static class HttpHeaderNames
{
    public const string CorrelationId = "X-Correlation-ID";
    public const string IdempotencyKey = "Idempotency-Key";
}
