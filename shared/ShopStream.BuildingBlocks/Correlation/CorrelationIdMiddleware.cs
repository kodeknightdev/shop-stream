using Microsoft.AspNetCore.Http;
using Serilog.Context;
using ShopStream.Contracts;

namespace ShopStream.BuildingBlocks.Correlation;

public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HttpHeaderNames.CorrelationId, out var values) && !string.IsNullOrWhiteSpace(values)
            ? values.ToString()
            : Guid.NewGuid().ToString("N");

        // so proxied requests carry it too
        context.Request.Headers[HttpHeaderNames.CorrelationId] = correlationId;
        CorrelationContext.CorrelationId = correlationId;
        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HttpHeaderNames.CorrelationId] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}

public sealed class CorrelationIdDelegatingHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var correlationId = CorrelationContext.CorrelationId;
        if (!string.IsNullOrEmpty(correlationId) && !request.Headers.Contains(HttpHeaderNames.CorrelationId))
        {
            request.Headers.Add(HttpHeaderNames.CorrelationId, correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
