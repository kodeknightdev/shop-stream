# react-frontend

Storefront and admin UI: React 18, Vite 4, TypeScript, React Router 6, TanStack Query 4, Tailwind 3.

| Route | Page |
|---|---|
| `/` | catalog with search, category & price filters, sorting, paging |
| `/products/:id` | product detail with stock level |
| `/cart`, `/checkout` | cart (stale price warning) and checkout with Stripe test cards |
| `/orders`, `/orders/:id` | history and live status timeline (polls until confirmed/cancelled) |
| `/admin` | sales dashboard, orders, products (CRUD), inventory (adjust stock) |

All calls go to `/api` on the same origin: Vite proxies to the gateway in development
(`VITE_GATEWAY_URL`, default `http://localhost:5000`), nginx does it in the container. Access tokens
are refreshed transparently on a 401.

```bash
npm ci
npm run dev      # http://localhost:5173
npm test         # vitest + testing-library
npm run build
```
