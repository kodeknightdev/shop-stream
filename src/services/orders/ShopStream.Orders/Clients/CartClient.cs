using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ShopStream.BuildingBlocks.Kafka;

namespace ShopStream.Orders.Clients;

public sealed record CartItem(Guid ProductId, string Name, int Quantity, decimal UnitPrice, string? ImageUrl);
public sealed record Cart(string UserId, IReadOnlyList<CartItem> Items);

public interface ICartClient
{
    Task<Cart?> GetCartAsync(string bearerToken, CancellationToken ct);
    Task ClearCartAsync(string bearerToken, CancellationToken ct);
}

// uses the caller's token, so cart needs no internal endpoint
public sealed class HttpCartClient : ICartClient
{
    private readonly HttpClient _http;

    public HttpCartClient(HttpClient http) => _http = http;

    public async Task<Cart?> GetCartAsync(string bearerToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/cart");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Cart>(JsonDefaults.Options, ct);
    }

    public async Task ClearCartAsync(string bearerToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/cart");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}
