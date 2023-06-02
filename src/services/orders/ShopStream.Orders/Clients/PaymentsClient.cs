using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ShopStream.BuildingBlocks.Kafka;

namespace ShopStream.Orders.Clients;

public sealed record PaymentInfo(Guid Id, Guid OrderId, decimal Amount, string Currency, string Provider, string Status,
    string? FailureReason, string? ProviderReference, DateTimeOffset CreatedAt);

public interface IPaymentsClient
{
    Task<PaymentInfo?> GetForOrderAsync(Guid orderId, string bearerToken, CancellationToken ct);
}

public sealed class HttpPaymentsClient : IPaymentsClient
{
    private readonly HttpClient _http;

    public HttpPaymentsClient(HttpClient http) => _http = http;

    public async Task<PaymentInfo?> GetForOrderAsync(Guid orderId, string bearerToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/payments/orders/{orderId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PaymentInfo>(JsonDefaults.Options, ct);
    }
}
