# Matloob

Next-generation Matloob platform. This repository replaces the legacy Laravel application `matloob-backoffice` with a .NET 10 API + Angular admin + PostgreSQL stack.

## Status

Phase 2 — backend skeleton. .NET 10 solution under [`backend/`](backend/) with FastEndpoints + OpenAPI + Serilog and a Docker Compose for local Postgres under [`docker/`](docker/). No EF Core, no IdM auth, no business endpoints yet. The Phase 0 planning docs in [`docs/`](docs/) remain the contract.

## Stack (target)

| Layer | Tech |
|---|---|
| API | .NET 10, FastEndpoints, EF Core, PostgreSQL, vertical-slice architecture |
| Admin UI | Angular LTS, PrimeNG, OIDC against NEC IdentityServer |
| Database | PostgreSQL 16 |
| File storage | Local disk (abstracted behind `IFileStorage`; S3 can be added later) |
| Auth | NEC IdentityServer (external, unchanged) |
| Deployment | Linux containers (default); managed or self-hosted Postgres |

## What stays external

- **NEC IdentityServer** lives in `eservices-backend` (separate repo). This API validates its JWTs but never modifies it.
- The **public-facing frontend website** is in a separate repo. We protect its API contract — see [`docs/20-api-compatibility-matrix.md`](docs/20-api-compatibility-matrix.md).

## What is removed from the legacy system

- **Ajeer** integration — removed in its entirety. Contracts and invoices that only existed for Ajeer are also removed. See [`docs/25-ajeer-disposition.md`](docs/25-ajeer-disposition.md).
- **Qiwa** as a runtime source of truth — removed. PostgreSQL is the source of truth for establishments.

## What is new

- **Manual establishment onboarding** is the core flow. User self-registers → uploads two required documents → admin approves → creator becomes Owner → can add members. Approved establishments edit via a separate ChangeRequest flow. Full spec in [`docs/15-establishment-onboarding-spec.md`](docs/15-establishment-onboarding-spec.md).
- **Local Assets API** replaces the old `signed-storage-url` S3 flow.

## Repository layout

```
matloob/
  README.md
  .gitignore
  .editorconfig
  docs/                         Phase 0 planning + future architecture/runbooks
    15-establishment-onboarding-spec.md
    20-api-compatibility-matrix.md
    25-ajeer-disposition.md
    30-data-migration-plan.md
  backend/                      .NET 10 API solution (Phase 2)
                                Open backend/Matloob.sln in Visual Studio 2022
  admin/                        Angular admin app (Phase 12)
                                Open admin/matloob-admin/ in VS Code
  docker/                       docker-compose for local dev (postgres, api, admin)
  scripts/                      developer utility scripts (seed, codegen, data-migrate)
```

## Legacy reference

The old Laravel system lives at [`d:/Sure/matloob-backoffice`](../matloob-backoffice). It is **read-only**. Do not modify it. It is the canonical source for:

- Business understanding
- API request/response shapes (see compatibility matrix)
- Database schema (see migration plan)
- Existing admin (Filament) screens being replaced by Angular admin

## Local development

### Prerequisites

- .NET 10 SDK (currently pinned at 10.0.102 in CI; any 10.0.x works)
- Docker Desktop (or any Docker engine + Compose v2)
- Visual Studio 2022 17.12+ (for opening `backend/Matloob.sln`) and/or VS Code

### Start PostgreSQL

```powershell
docker compose -f docker/docker-compose.yml up -d
docker compose -f docker/docker-compose.yml ps      # confirms "healthy"
```

Connection string (the .NET API will read this from `appsettings.Development.json` in Phase 3):

```
Host=localhost;Port=54321;Database=matloob;Username=matloob;Password=matloob_dev_password
```

Stop and remove the container (data preserved in the named volume):

```powershell
docker compose -f docker/docker-compose.yml down
```

Wipe the data volume too (use when you want a fresh database):

```powershell
docker compose -f docker/docker-compose.yml down -v
```

### Run the API

```powershell
dotnet run --project backend/src/Matloob.Api
```

The API binds to `http://localhost:5180` (set in `Properties/launchSettings.json`). Useful URLs:

| URL | Purpose |
|---|---|
| `http://localhost:5180/health` | Liveness — returns `Healthy` |
| `http://localhost:5180/health/ready` | Readiness — returns `Healthy` (will check DB + IdM in Phase 3+) |
| `http://localhost:5180/api/v1/system/ping` | Framework smoke endpoint — returns `{"status":"ok"}` |
| `http://localhost:5180/swagger` | Swagger UI (Development only) |
| `http://localhost:5180/swagger/v1/swagger.json` | OpenAPI document |

### Run the tests

```powershell
dotnet test backend/Matloob.sln
```

## Next phase

Phase 3 — EF Core + Npgsql + `BaseAuditableEntity` + audit/soft-delete interceptors + initial migration. The `/health/ready` endpoint will then probe the live Postgres connection.
