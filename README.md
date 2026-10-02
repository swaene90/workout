# Workout

A mobile-first workout tracker for two brothers. The overview puts weekly streaks first: check in with one tap, see both people's Monday–Sunday activity, and keep a realistic weekly goal. Detailed strength/cardio logs, reusable templates, and exercise progress are included.

## Stack

- React + TypeScript + Vite; phone-first layouts, bottom navigation, and touch-sized controls.
- ASP.NET Core 10 + EF Core 10 + Npgsql; React is served by the API in production.
- PostgreSQL on the existing server at `192.168.0.48:5432`, in a dedicated `workout` database.
- Google OpenID Connect; only `swaene1@gmail.com` and `swaene15@gmail.com` can sign in.
- Docker Compose for the app, migration job, and optional Cloudflare Tunnel.

## Quick preview on this laptop

```powershell
.\scripts\dev.ps1 demo
```

Open `http://127.0.0.1:5173/demo`, or select **Explore the demo** at the bottom right of the login screen. The `/demo` route is available in development and production. It uses browser-only mock workouts, streaks, templates, drafts, progress, and goal history, with working check-ins and editors. Changes reset on refresh; demo appearance preferences are remembered separately in this browser. **Exit demo** returns to the normal application. Demo requests never call the backend, and real `/api` endpoints still require authentication.

The setup installed .NET 10 SDK, Node 24 with npm, and GitHub CLI under the ignored `.tools` directory. `scripts/dev.ps1` finds these tools automatically. Docker Desktop is installed separately and already runs Linux containers. On another machine, install .NET 10 SDK, Node 24 LTS, Git, and Docker with Compose; run `npm ci` in `frontend`.

## Local application

Copy `.env.example` to `.env` and provide real configuration. On the original laptop, `.env` already contains the dedicated database connection; do not replace it with the example. `.env`, `secrets.txt`, and `.tools` are excluded from Git and the Docker build context.

```powershell
docker compose -f compose.yaml -f compose.local.yaml up --build -d --wait
```

Open `http://localhost:5080`. This starts the migration job before the app and binds the app only to localhost. Add Google OAuth credentials as described below to enable sign-in.

For separate development servers, first apply migrations using Compose, then run these commands in separate terminals:

```powershell
.\scripts\dev.ps1 api
.\scripts\dev.ps1 frontend
```

The frontend at `http://127.0.0.1:5173` proxies API and auth routes to the backend at port 5080. Use `http://localhost:5173` for OAuth development and register its callback. The API uses development cookies only in the local development configuration.

## Streak rules

- Everyone starts with a goal of 3 workout days per week; goals can be 1–7 days.
- Dates and Monday–Sunday weeks use `America/New_York`, including daylight-saving changes.
- Multiple completed sessions on a date count as one day. Drafts never count.
- Meeting the current week's goal immediately extends the streak. An unfinished current week preserves the preceding streak until Monday begins; a missed past week breaks it.
- Edits, deletions, and backdated entries recalculate current and longest streaks. Future completed workouts are rejected.
- Goal changes take effect the following Monday, preserving historical goals.
- Templates prefill a draft; explicitly mark the workout completed when saving to make it count.
- Each person can read both workout histories but modify only their own workouts, templates, and goal. Templates are personal.
- Strength uses reps and pounds; cardio uses minutes and optional miles. Zero-pound sets support bodyweight exercises.

## Appearance

Settings offers green, red, blue, beige, and purple themes, each with light and dark modes. The palettes share soft backgrounds, tinted cards, and calm accents, with large touch targets for phones. Each person's choice is saved to their account independently and restored across devices after login. A browser cache applies the last choice before the page renders.

The protected `PUT /api/preferences` endpoint accepts `{ theme, mode }` and updates only the signed-in member. Valid themes are `green`, `red`, `blue`, `beige`, and `purple`; modes are `light` and `dark`. Like other writes, this requires an antiforgery token.

## Google login

1. Create a Google Cloud OAuth client of type **Web application**. Configure the consent screen for this personal app; while in Testing, add both allowed emails as test users. Request only `openid`, `email`, and `profile`.
2. Register exact authorized redirect URIs:
   - `http://localhost:5080/signin-google` for the local Compose app.
   - `http://localhost:5173/signin-google` for the frontend development proxy.
   - `https://YOUR_PUBLIC_HOSTNAME/signin-google` for the deployed app.
3. Set `Authentication__Google__ClientId` and `Authentication__Google__ClientSecret` in the ignored `.env`, then recreate the app container.

The backend validates Google's token, requires a verified allowlisted email, and binds it to Google's stable subject ID. It keeps credentials and tokens out of the browser, uses HttpOnly session cookies, and requires an antiforgery token for API writes. Other Google accounts cannot create app sessions. There is no public signup or production test-login endpoint.

## Database provisioning

The application uses **`workout`**, not the existing `fantfoot` database. The original laptop has been configured with a newly generated `workout_app` role password, stored only in `.env`.

For a fresh server, a PostgreSQL administrator can run this, supplying a unique password privately:

```sql
CREATE ROLE workout_app LOGIN PASSWORD 'REPLACE_PRIVATELY'
    NOSUPERUSER NOCREATEDB NOCREATEROLE;
CREATE DATABASE workout OWNER workout_app;
-- Connect to workout before running this:
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
```

The app role owns this database so the one-shot migration job can manage its schema. No server-wide privileges are required at runtime. Configure PostgreSQL to listen on its LAN interface and allow the application's host in `pg_hba.conf`; keep port 5432 on the private network. If PostgreSQL is configured with a trusted TLS certificate, add `SSL Mode=VerifyFull` and the appropriate root certificate to the connection string.

The optional `scripts/DatabaseSetup` helper reads `secrets.txt` locally and creates the dedicated database and role with a generated password. Run `dotnet run --project scripts/DatabaseSetup` from the repository root. It stops if either object already exists, rather than changing an existing role/database. Its generated connection string is written only to `.env`.

EF migrations are checked in. Apply them via the Compose `migrate` service or `dotnet run --project backend/Workout.Api -- --migrate` with the connection string in the environment. The migration job exits nonzero on failure, preventing app startup.

## Home-server deployment with Cloudflare Tunnel

1. Install Docker Engine with Compose on the home server and clone this repository.
2. Create a private `.env` with the database connection, Google credentials, `PUBLIC_HOSTNAME`, and `TUNNEL_TOKEN`. Transfer the local credentials through a private channel, not Git.
3. Create a remotely managed tunnel in Cloudflare. Configure a public hostname pointing to **`http://app:8080`**. Use a domain you control in Cloudflare, and put its hostname in `PUBLIC_HOSTNAME` without `https://`.
4. Register that hostname's Google callback, then start:

```sh
docker compose --profile remote up --build -d --wait
docker compose ps
```

The remote configuration publishes no inbound host ports. Cloudflare provides public HTTPS; the app trusts forwarded headers only from the tunnel's fixed internal address `172.30.0.3`. `/health/live` checks the process and `/health/ready` checks database connectivity. Authentication/API responses are not cached.

The Compose network uses `172.30.0.0/24`. If it overlaps another network on your server, change the subnet and all three static addresses together, including `Proxy__TrustedIp`. The `data-protection` volume persists cookie-protection keys across restarts. Preserve that volume; restrict server access because it contains authentication key material.

Updates are manual: pull the approved commit, back up the database, and rerun Compose. No automated production deployment or server credentials are stored in GitHub.

## Tests

```sh
docker compose -f compose.test.yaml up -d --wait
```

Then set `WORKOUT_TEST_POSTGRES` to the isolated test database and run:

```powershell
$env:WORKOUT_TEST_POSTGRES='Host=localhost;Port=15432;Database=workout_tests;Username=workout_test;Password=local-test-only'
.\scripts\dev.ps1 test
```

Or use standard tools: `dotnet test backend/Workout.Tests`, `npm test` and `npm run build` in `frontend`. Without the test connection, the PostgreSQL-specific test explicitly skips; other tests still run.

The isolated test container stores its database in disposable memory and never uses the LAN database. Stop it after testing with `docker compose -f compose.test.yaml down`. CI runs backend/API/streak tests, relational migration/persistence tests, frontend behavior tests, and the full Docker image build.

## Backup and rollback

Use a PostgreSQL client with a private `PGPASSFILE` or interactive password prompt; do not put passwords in shell history:

```sh
pg_dump -h 192.168.0.48 -p 5432 -U workout_app -d workout -Fc -f workout-backup.dump
```

Take a backup before every schema update. Keep the backup and the `data-protection` volume snapshot in private storage outside the repository. To roll back, stop the app/tunnel, check out the previous release commit, and rebuild. If the old release is incompatible with the new schema, have the database administrator restore the matching backup using `pg_restore` into a replacement database, update the private connection string, then start Compose. Database restore loses changes made after the backup; do not restore over a live database.

## API

Authenticated JSON endpoints live under `/api`: `/me`, `/dashboard`, `/workouts`, `/templates`, `/progress?userId=…&exercise=…`, `/goals`, and `/logout`. Workout history supports `userId`, `from`, `to`, and `page` (30 sessions/page). Obtain `csrfToken` from `/api/me` and send it as `X-CSRF-TOKEN` on writes. Invalid input returns problem-details validation responses; unauthorized/forbidden calls return 401/403. `/auth/login` starts Google login, and `/signin-google` is handled by the OIDC middleware.
