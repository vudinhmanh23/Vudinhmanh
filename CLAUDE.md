# SalesInventory

Sales & inventory management system (graduation project): ASP.NET Core 8 Web API + Blazor Server UI + EF Core + SQL Server.
Includes an AI sales assistant (Anthropic Messages API, RAG with Voyage embeddings).

## Layout
- `src/SalesInventory.Api` - Web API (controllers, middleware, `Hardening/`, `Security/`, `Logging/`). Entry point `Program.cs`.
- `src/SalesInventory.Application` - services, DTOs, validators, PDF/Excel reports, assistant tools.
- `src/SalesInventory.Infrastructure` - EF Core `AppDbContext` + migrations, Identity, AI clients, secret masking.
- `src/SalesInventory.Domain` - entities.
- `src/SalesInventory.Web` - Blazor Server UI. It calls the API from the server side (`ApiBaseUrl`).
- `tests/SalesInventory.Tests` (unit) and `tests/SalesInventory.Api.Tests` (integration, real SQL Server in a Testcontainers container).

## Commands
```
dotnet build
dotnet test                                   # integration tests need Docker running
SALESINVENTORY_TESTS_DB=InMemory dotnet test  # without Docker (transaction/race tests are then not meaningful)
docker compose up --build                     # whole stack; needs .env (copy .env.example)
```

## Conventions
- Code comments in English.
- Never commit secrets. Dev: `dotnet user-secrets` (project `src/SalesInventory.Api`). Production/Docker: environment variables only.
- Never log secrets: no tokens, passwords, API keys, connection strings, query strings or request bodies (see `Logging/` and `SecretMasker`).

## Configuration and environments
Settings are layered, later wins: `appsettings.json` -> `appsettings.{Environment}.json` -> user-secrets (Development only) -> **environment
variables** -> command line. In environment variable names `__` stands for `:` (`ConnectionStrings__DefaultConnection` = `ConnectionStrings:DefaultConnection`).

- `appsettings.json` / `appsettings.Production.json` hold **non-sensitive** keys only. In Production the API **refuses to start** if a
  secret (`Jwt:Key`, `ConnectionStrings:DefaultConnection`, `Anthropic:ApiKey`, `Embeddings:ApiKey`, `SeedAdmin:Password`) comes from a
  settings file, or if the connection string is missing (`Hardening/SecretSettingsGuard.cs`). A test also scans the repo's settings files.
- Production turns on: HTTPS redirection (308), HSTS (1 year), generic error responses (no exception details), CORS only for the exact
  origins listed, forwarded headers only from trusted proxies. Development keeps Swagger, detailed errors and no HSTS.

### Environment variables - API (`SalesInventory.Api`)
| Variable | Required | Secret | Meaning |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | yes | no | `Production` on a server, `Development` locally. docker-compose defaults to `Development` (Swagger on). |
| `ConnectionStrings__DefaultConnection` | yes | **yes** | SQL Server connection string, e.g. `Server=...;Database=SalesInventoryDb;User Id=...;Password=...;TrustServerCertificate=True;` |
| `Jwt__Key` | yes | **yes** | HMAC key that signs access tokens: 32+ random characters, different per environment. |
| `SeedAdmin__Email`, `SeedAdmin__Password` | no | password **yes** | First Admin account, created at start-up when both are set. Remove after first login in a real deployment. |
| `ANTHROPIC_API_KEY` (or `Anthropic__ApiKey`) | for the assistant | **yes** | Without it the assistant answers with an error; the rest of the API works. |
| `VOYAGE_API_KEY` (or `Embeddings__ApiKey`) | for RAG | **yes** | Embeddings provider key. |
| `Cors__AllowedOrigins__0` (`__1`, ...) | if a browser app calls the API directly | no | Exact origin, https only, no path, no `*`. Empty = no CORS. The Blazor Server UI calls from the server and needs none. |
| `Hardening__TrustedProxies__0` | behind a reverse proxy | no | IP or CIDR of the proxy that ends TLS (e.g. `172.18.0.0/16`); only it is believed about `X-Forwarded-Proto`. |
| `ASPNETCORE_HTTPS_PORT` | behind a TLS proxy | no | Public https port used by the HTTP->HTTPS redirect (usually `443`). Do not set it for plain-http local runs. |
| `Database__MigrateOnStartup` | no | no | `true` applies EF Core migrations at start-up (docker-compose sets it). Default `false`: a production database is never changed by just starting the app. |
| `AllowedHosts` | recommended | no | Host names the API answers to, e.g. `api.example.com`. Default `*`. |
| `Security__AuthFailures__AlertThreshold`, `...__WindowMinutes` | no | no | 401/403 alert: failures per client IP within the window (default 10 in 5 min). |

### Environment variables - Blazor (`SalesInventory.Web`)
| Variable | Required | Meaning |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | yes | `Production` on a server. |
| `ApiBaseUrl` | yes | Where the Blazor **server** reaches the API. In docker-compose: `http://api:8080`. The default in `appsettings.json` (`https://localhost:7028`) is for local dev only. |

### docker-compose (`.env`, copied from `.env.example`, never committed)
`MSSQL_SA_PASSWORD` (required, SQL Server `sa` password), `JWT__KEY` (required), `SEEDADMIN__EMAIL`, `SEEDADMIN__PASSWORD`,
`ANTHROPIC_API_KEY`, `CORS__ALLOWEDORIGINS__0`, `HARDENING__TRUSTEDPROXIES__0`, `API_PORT` (5080), `WEB_PORT` (5081).
compose builds `ConnectionStrings__DefaultConnection` itself from `MSSQL_SA_PASSWORD` (host `sqlserver`).

### Local development
```
dotnet user-secrets set "Jwt:Key" "<random 32+ chars>" --project src/SalesInventory.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<your local connection string>" --project src/SalesInventory.Api
dotnet user-secrets set "Anthropic:ApiKey" "<key>" --project src/SalesInventory.Api     # optional
```
