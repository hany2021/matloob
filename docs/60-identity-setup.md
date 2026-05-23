# 60 · Local Identity setup

The Matloob backend + Angular admin both authenticate against the NEC
IdentityServer (Duende, multi-project solution at
`d:/Sure/eservices-backend/`). This document is the one-time setup so a
fresh checkout can run end-to-end login.

## What runs where

| Service | URL | Source |
| --- | --- | --- |
| PostgreSQL (Matloob data) | `localhost:54321` | docker compose at repo root |
| Matloob API | `http://localhost:5180` | `backend/src/Matloob.Api` (`dotnet run`) |
| Angular admin | `http://localhost:4200` | `admin` (`npm start`) |
| **IdentityServer STS** (token issuer) | `https://localhost:44310` | `eservices-backend/src/NEC.IdentityServer.STS.Identity` |
| IdentityServer Admin UI | `https://localhost:44303` | `eservices-backend/src/NEC.IdentityServer.Admin` (optional) |
| SQL Server (IdentityServer DB) | `localhost:1433` | docker compose at `d:/Sure/EServicesPortal (1)/` |

## One-time setup

### 1. SQL Server + IdentityServer databases

The IdentityServer projects need three SQL Server databases:
`IdentityServerAdmin`, `EServicesPortal`, `NecHangfireDashboard`. The
`.bak` files live at `d:/Sure/EServicesPortal (1)/`.

```powershell
cd "d:/Sure/EServicesPortal (1)"
docker compose up -d
```

That spins up `eservices-mssql` (port 1433) and runs the restore for
`IdentityServerAdmin` + `NecHangfireDashboard`. `EServicesPortal` is
restored separately:

```powershell
docker exec -it eservices-mssql /bin/bash -c '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "EServ1ces@2025!" -C -No -i /var/opt/mssql/backup/_restore_eservices.sql'
```

(or copy `_restore_eservices.sql` into the container and run it.)

Verify:

```powershell
docker exec -it eservices-mssql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "EServ1ces@2025!" -C -No -Q "SELECT name FROM sys.databases ORDER BY name"
```

You should see `IdentityServerAdmin`, `EServicesPortal`, and
`NecHangfireDashboard`.

### 2. Seed Matloob ApiResources + base clients

`d:/Sure/eservices-backend/_tmp_matloob_register.sql` registers:

- ApiResources / ApiScopes: `matloob:api`, `matloob:admin`
- Two clients: `matloob:admin` (Filament back-office, ports 8000/8080),
  `matloob:front-dev` (SPA, ports 3000/3001)
- Roles: `matloob_admin`, `matloob_user`, `matloob_establishment_user`
- Grants the existing `admin` user the `matloob_admin` role
  (password is `Pa$$word123` from the seeded DB)

Apply against the restored DB:

```powershell
docker exec -i eservices-mssql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "EServ1ces@2025!" -C -No -d IdentityServerAdmin < "d:/Sure/eservices-backend/_tmp_matloob_register.sql"
```

### 3. Register the Angular admin client (port 4200)

The Angular admin SPA needs its own client because its redirect URI
(`http://localhost:4200/auth/callback`) doesn't match either of the
clients seeded in step 2. Run the additive seed in this repo:

```powershell
docker exec -i eservices-mssql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "EServ1ces@2025!" -C -No -d IdentityServerAdmin < "d:/Sure/matloob/docs/setup/identity-server-matloob-angular-admin.sql"
```

That script is idempotent — re-running is safe. It registers:

| Field | Value |
| --- | --- |
| `ClientId` | `matloob:admin-angular` |
| Grant type | `authorization_code` + PKCE, no client secret |
| `RedirectUri` | `http://localhost:4200/auth/callback` |
| `PostLogoutRedirectUri` | `http://localhost:4200/auth/logout` |
| `AllowedCorsOrigins` | `http://localhost:4200` |
| Scopes | `openid`, `profile`, `email`, `roles`, `matloob:api`, `matloob:admin` |

### 4. STS connection strings

`NEC.IdentityServer.STS.Identity/appsettings.Development.json` ships
with `Server=localhost;Trusted_Connection=True` (Windows Auth to a
locally-installed SQL Server). If you're using the Docker SQL Server
from step 1, override the connection string at runtime via env vars
**without modifying the file**:

```powershell
# from d:/Sure/eservices-backend/src/NEC.IdentityServer.STS.Identity
$env:ConnectionStrings__ConfigurationDbConnection = 'Server=localhost,1433;Database=IdentityServerAdmin;User Id=sa;Password=EServ1ces@2025!;TrustServerCertificate=True;Encrypt=False'
$env:ConnectionStrings__PersistedGrantDbConnection = $env:ConnectionStrings__ConfigurationDbConnection
$env:ConnectionStrings__IdentityDbConnection       = $env:ConnectionStrings__ConfigurationDbConnection
$env:ConnectionStrings__AdminLogDbConnection       = $env:ConnectionStrings__ConfigurationDbConnection
$env:ConnectionStrings__AdminAuditLogDbConnection  = $env:ConnectionStrings__ConfigurationDbConnection
$env:ConnectionStrings__DataProtectionDbConnection = $env:ConnectionStrings__ConfigurationDbConnection
$env:ConnectionStrings__HangfireConnection         = 'Server=localhost,1433;Database=NecHangfireDashboard;User Id=sa;Password=EServ1ces@2025!;TrustServerCertificate=True;Encrypt=False'
dotnet run
```

> Skip this if you have a local SQL Server install and Windows Auth
> works (Express / Developer edition with `Trusted_Connection=True`).

### 5. Trust the dev HTTPS certificate

```powershell
dotnet dev-certs https --trust
```

Verify the discovery document loads:

```powershell
curl -k https://localhost:44310/.well-known/openid-configuration
```

Should return a JSON document with `issuer`, `jwks_uri`,
`authorization_endpoint`, etc.

## Daily startup

After the one-time setup above:

```powershell
# 1. SQL Server (IdentityServer DBs)
cd "d:/Sure/EServicesPortal (1)" ; docker compose up -d

# 2. PostgreSQL (Matloob data) — already running in your stack
#    docker ps should show matloob-postgres on :54321

# 3. IdentityServer STS
cd d:/Sure/eservices-backend/src/NEC.IdentityServer.STS.Identity
# (set the env vars from step 4 above if using docker SQL)
dotnet run

# 4. Matloob API
cd d:/Sure/matloob/backend/src/Matloob.Api
dotnet run

# 5. Angular admin
cd d:/Sure/matloob/admin
npm start
```

Open `http://localhost:4200/auth/login`, click **Sign in**, log in as
`admin` / `Pa$$word123`. The callback round-trips back to
`/dashboard`; the topbar should show the user and an "admin" badge.

## Token shape (debug aid)

The Angular admin asks for both `matloob:api` and `matloob:admin`
scopes. The access token comes back with `aud: ["matloob:api",
"matloob:admin"]`. The backend's `Policy.User` accepts either audience;
`Policy.Admin` additionally requires the `matloob:admin` audience plus
the `matloob_admin` role. See
[`AuthRegistration.cs`](../backend/src/Matloob.Api/Infrastructure/Auth/AuthRegistration.cs).

## Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| `ERR_CONNECTION_REFUSED` on `/.well-known/openid-configuration` | STS isn't running, or `identityAuthority` in `environment.ts` points at the wrong host/port |
| `invalid_redirect_uri` in IdentityServer | The `matloob:admin-angular` client isn't in the restored DB — re-run the seed in step 3 |
| 401 from `/api/v1/profile` immediately after login | Token `aud` doesn't include `matloob:api` — the client's `ClientScopes` row is missing `matloob:api` |
| 403 from `/api/v1/admin/*` while regular routes work | Token `aud` doesn't include `matloob:admin` — either the client scope is missing or the Angular env's `scope` doesn't list `matloob:admin` |
| `NET::ERR_CERT_AUTHORITY_INVALID` in the browser | `dotnet dev-certs https --trust` hasn't been run, or the browser session needs a hard reload |
| `Cannot open database "IdentityServerAdmin"` | SQL Server is up but the .bak hasn't been restored — see step 1 |
