# identity

Users, roles and tokens. Issues short-lived JWT access tokens (HS256, 15 min) and rotating refresh
tokens (7 days, stored as SHA-256 hashes). Replaying an already-rotated refresh token revokes every
active token for that user.

## Endpoints

| Method | Path | Auth | Notes |
|---|---|---|---|
| POST | `/api/auth/register` | - | creates a `Customer`, returns tokens |
| POST | `/api/auth/login` | - | rate limited at the gateway |
| POST | `/api/auth/refresh` | - | rotates the refresh token |
| POST | `/api/auth/logout` | - | revokes the given refresh token |
| GET | `/api/auth/me` | user | current profile |
| GET | `/api/users?search=&page=` | Admin | user list |
| PUT | `/api/users/{id}/role` | Admin | `Admin` or `Customer` |
| GET | `/health`, `/health/ready` | - | ready checks Postgres |

Swagger: `/swagger` (or through the gateway at `/swagger` → *identity*).

Token claims: `sub` (user id), `email`, `name`, `role`, `jti`. Every other service validates the
same issuer/audience/key.

## Kafka

None - identity is purely synchronous.

## Data

`identity_db`: `users`, `refresh_tokens`. Migrations in `Data/Migrations` are applied automatically in
Development or when `Database__MigrateOnStartup=true`.

In **Development only**, the seeder creates the demo accounts `admin@shopstream.local`,
`customer@shopstream.local` and `jane@shopstream.local`, all with the password from
`SEED_DEMO_PASSWORD` (`Demo@12345` in `.env.example` - local demo data). Their ids are fixed in
`ShopStream.Contracts.DemoAccounts`, so orders and payments can seed Jane's history.

## Run

```bash
dotnet run --project src/services/identity/ShopStream.Identity   # http://localhost:5001 (launch profile sets SEED_DEMO_PASSWORD)
```

Config: `ConnectionStrings__IdentityDb`, `Jwt__SigningKey`, `Jwt__AccessTokenMinutes`, `Jwt__RefreshTokenDays`.
