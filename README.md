# Matloob

Next-generation Matloob platform. This repository replaces the legacy Laravel application `matloob-backoffice` with a .NET 10 API + Angular admin + PostgreSQL stack.

## Status

Phase 1 — repository skeleton. No code, no solution, no Angular app yet. The Phase 0 planning docs in [`docs/`](docs/) are the contract.

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

## Next phase

Phase 2 — initialize the .NET 10 solution under `backend/`. See [`docs/`](docs/) for the full phased plan.
