# gateway (YARP)

Single entry point for the frontend. Routes are configured in `appsettings.json` under
`ReverseProxy`; cluster addresses are overridden per environment
(`ReverseProxy__Clusters__<name>__Destinations__primary__Address`).

| Path | Cluster | Policy |
|---|---|---|
| `/api/auth/{login,register,refresh}` | identity | `auth` rate limit (10/min per IP) |
| `/api/auth/**` | identity | - |
| `/api/users/**` | identity | admin |
| `/api/categories/**`, `/api/products/**` | catalog | - (writes checked by catalog) |
| `/api/cart/**` | cart | authenticated |
| `/api/orders/**` | orders | authenticated |
| `/api/admin/**` | orders | admin |
| `/api/payments/**` | payments | - (checked by payments) |
| `/api/inventory/**` | inventory | - (admin routes checked by inventory) |
| `/api/emails/**` | email | admin |

- **JWT** is validated here first; services validate again (defence in depth).
- **Rate limiting** uses the built-in .NET 7 limiter: fixed window, 120 requests / 10 s per client IP
  for everything, plus a sliding window for login/register/refresh. Rejections are 429 problem
  details with `Retry-After`.
- **Correlation id** - `X-Correlation-ID` is generated if missing and forwarded downstream.
- **Swagger** - `/swagger` shows every service's OpenAPI document, proxied through `/docs/<service>/...`.
- `/health/ready` pings every downstream `/health` and reports `Degraded` if one is down.

## Run

```bash
dotnet run --project src/gateway/ShopStream.Gateway   # http://localhost:5000/swagger
```
