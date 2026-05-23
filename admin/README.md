# Matloob Admin

Angular front-end for the Matloob back-office. Talks to the new .NET API
(`backend/`) via the canonical `/api/v1` routes.

## Stack

- Angular 21 (LTS)
- Standalone components, strict TypeScript
- SCSS, hand-rolled design tokens (no UI library — small enough that the
  cost-vs-flexibility tradeoff favors hand-rolled)
- OIDC via `angular-oauth2-oidc`
- HTTP via Angular's built-in `HttpClient` + interceptors

## Running locally

```bash
npm install
npm start            # ng serve, http://localhost:4200
npm run build        # production build
npm test             # vitest
```

## Environments

Configuration lives in `src/environments/environment.ts` (development) and
`src/environments/environment.prod.ts` (production). The production file
is swapped in automatically by Angular CLI's `fileReplacements`.

| Variable | Description |
|---|---|
| `apiBaseUrl` | Root URL of the .NET backend (no trailing slash). |
| `identityAuthority` | IdentityServer base URL. |
| `clientId` | OAuth client id. |
| `redirectUri` | OAuth redirect callback. |
| `postLogoutRedirectUri` | Post-logout target. |
| `scope` | OAuth scopes. |

Never commit real production values. Replace placeholders at deploy time
(e.g. via env-substitution at container startup or a build-time script).

## Project layout

```
src/app/
  core/         -- singletons: auth, http, config, models
  shared/       -- reusable components, forms, table helpers
  layout/       -- admin shell (sidebar, topbar)
  auth/         -- login / callback / logout pages
  features/    -- one folder per business slice
    dashboard/
    profile/
    establishments/
    assets/
    opportunities/
    applications/
    offers/
    evaluations/
```

## Backend contract

Use the canonical `/api/v1/...` routes. Legacy `/api/{users|establishments}/...`
routes exist for the old Laravel frontend's compatibility and should not
be used here. See `docs/41-legacy-request-compatibility.md` for the full
audit.
