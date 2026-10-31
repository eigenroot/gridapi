# GRID API

The backend for GRID Home (consumer) and GRID Pro (provider). Version 1 covers:

- **Phone + OTP sign-in** shared by both apps.
- **One consumer record per phone number.**
- **Background telco name lookup (KYC).**
- **Homes and saved utility accounts.**
- **GRID Pro onboarding**: provider accounts, the application and its readiness and submission, offerings, service areas, availability, portfolio, verification documents, standards, payout methods, and media upload.
- **The service catalogue**: cleaning and laundry, and the service areas.

Client calling conventions are in [`docs/api-integration.md`](docs/api-integration.md).

.NET 10 · ASP.NET Core minimal APIs · EF Core 10 + PostgreSQL 17 · JWT bearer · Scalar.

## Production deployment

The API repository is `eigenroot/gridapi`. GitHub Actions builds and tests pull
requests. Successful updates to `main`, including merged pull requests, publish a
Linux AMD64 image to `ghcr.io/eigenroot/gridapi`, create a versioned GitHub Release
with generated notes, and deploy that exact commit to the Droplet. Releases begin
at `v0.1.0` and automatically increment the patch version; rerunning one commit
reuses its existing release. The API runs behind Caddy at
`https://grid-api.eigenroot.com`. See [the deployment guide](deploy/README.md).

Connection strings and application/provider secrets belong in .NET user-secrets
for local development and `/opt/grid/.env` on the server. They are not included
in the GitHub repository or Docker image. Neon CLI environment files are ignored.

`npm run build` runs the .NET solution build. Node dependencies support Neon's
infrastructure configuration; the API itself runs on .NET.

## Run it

```sh
docker compose up -d                       # PostgreSQL with a grid_api database (grid/grid)
dotnet run --project src/Grid.Api          # http://localhost:8080, migrates + seeds on start
```

If you run PostgreSQL yourself, point the API at it without editing tracked files:

```sh
createdb grid_api
dotnet user-secrets set "ConnectionStrings:Grid" "Host=localhost;Database=grid_api;Username=$USER" --project src/Grid.Api
```

Browse the API at `http://localhost:8080/scalar/v1` (OpenAPI JSON at `/openapi/v1.json`), and check health at `/health`.

**Development defaults** (`appsettings.Development.json`):

- SMS goes to the log (`Sms:Provider=Log`).
- KYC is off.
- OTPs are randomly generated. Fixed `123456` codes require explicitly enabling
  `Otp:DevelopmentMode` locally; this mode is rejected in Production.

Set `Jwt:SigningKey`, `Otp:Pepper`, and `Payouts:EncryptionKey` in local user-secrets
before starting a fresh checkout. Generate each independently with
`openssl rand -base64 32`, then save with
`dotnet user-secrets set "KEY" "VALUE" --project src/Grid.Api`.
Existing workstation credentials were preserved in user-secrets during deployment setup.

The seeded consumer is `233240000001` (Ama Mensah), with a home and three saved accounts.

## Tests

```sh
dotnet test
```

The integration tests create and drop a throwaway database per test class on a real PostgreSQL. By default they connect to `localhost` as your OS user; set `GRID_TEST_DB` (a connection string without `Database=`) to use another server.

## Configuration

Every key can be set from the environment with `__` for `:`, e.g. `Sms__Provider=Hubtel`. Options are validated at startup, so a missing secret stops the app rather than failing on the first request.

| Key | Purpose |
|---|---|
| `ConnectionStrings:Grid` | PostgreSQL |
| `Jwt:SigningKey` | ≥ 32 bytes. Access tokens last `Jwt:AccessTokenMinutes` (30), refresh tokens `Jwt:RefreshTokenDays` (60) |
| `Otp:Pepper` | ≥ 32 chars, mixed into OTP hashes |
| `Otp:DevelopmentMode` | Fixed code `123456`; **the app refuses to start with this on in Production** |
| `Otp:ReviewNumbers:{phone}` | Fixed code for app-store review accounts; no SMS is sent to them |
| `Sms:Provider` | `Arkesel`, `Hubtel` or `Log` |
| `Sms:FallbackProvider` | Optional. Tried once if the primary fails |
| `Sms:SenderId` | ≤ 11 chars, must be approved on each provider account |
| `Sms:Arkesel:ApiKey`, `Sms:Arkesel:Sandbox` | Arkesel main SMS API key. Sandbox sends are free and never delivered |
| `Sms:Hubtel:ClientId`, `Sms:Hubtel:ClientSecret` | Hubtel SMS credentials |
| `Kyc:Provider` | `Hubtel` or `None` |
| `Kyc:Hubtel:ApiId`, `ApiKey`, `CollectionAccountNumber` | Hubtel Verification API. **Calls only work from IPs Hubtel has whitelisted** |
| `ReverseProxy:TrustForwardedHeaders` | `true` behind Caddy, so rate limits see the real client IP |
| `Database:MigrateOnStartup` | Defaults to on in Development only |
| `Media:RootPath` | Where uploaded files are stored (`App_Data/media` by default; mount a volume in production) |
| `Media:MaxBytes`, `Media:TicketMinutes` | Upload limit (25 MB) and how long an upload link stays valid (15 min) |
| `Media:PublicBaseUrl` | Optional origin for upload URLs. Without it, the request's scheme and host are used |
| `Payouts:EncryptionKey` | Base64 of 32 random bytes (`openssl rand -base64 32`). It encrypts payout account numbers. **Losing it makes the stored numbers unreadable** |

## How it fits together

- **OTP is ours; SMS providers only deliver it.**
  - The API generates the code, stores only an HMAC of it, and checks it itself.
  - Switching `Sms:Provider` never invalidates a code already sent, and the fallback provider can resend the same code.
  - Arkesel's own OTP API is deliberately unused, since Hubtel has no equivalent.
- **Abuse limits.**
  - 60 s between codes and 5 codes per phone per hour.
  - 5 wrong guesses per code.
  - A per-IP fixed window on both OTP endpoints.
- **Consumers.**
  - The primary key is a UUID; `phone_number` (`233XXXXXXXXX`) is unique. Inputs in `0…`, `+233…` or spaced forms are normalised at the edge.
  - Self-provided names (`first_name`, `last_name`) and the telco name (`kyc_name`, with `kyc_status`, `kyc_provider` and `kyc_checked_at`) live in separate columns and never overwrite each other.
- **KYC is optional and in the background.**
  - `KycWorker` polls `consumers` for `NotChecked` rows and for `Pending` rows that are due, calls the lookup, and retries transient failures on a backoff of 1m → 5m → 30m → 2h. After 5 attempts it marks the row `Failed`.
  - With `Kyc:Provider=None` nothing calls out. Turning KYC on later works through everyone who signed up meanwhile.
  - KYC data is never returned to the apps.
- **Refresh tokens** are single-use and rotate. Presenting an already-used one revokes the whole session.
- **Provider accounts.**
  - A consumer owns at most one provider account. `user.providerAccountId` is how the apps tell that someone is a provider; there are no roles in the token.
  - The application moves `Draft → Submitted → UnderReview → Approved | MoreInformationRequired | Rejected`, and its status is mirrored onto the account's `verificationStatus`.
  - Each submit stores a jsonb snapshot in `provider_application_submissions`, and a resubmission bumps `version`.
  - While an application is `Submitted`, `UnderReview` or `Approved`, the account details, experience, offerings and verification documents are locked (`409 providers.locked`).
- **Readiness** is computed by the server (`ProviderReadiness`). The submit flow in GRID Pro sends the offering before its price exists, so a missing price is a readiness item rather than a validation error. GRID Pro collects a selfie rather than a separate profile photo, and either one satisfies the profile-photo requirement.
- **Media.**
  - Bytes go to `IMediaStore`, which is a host directory in v1.
  - Keys are flat (`{32 hex}.{ext}`).
  - Confirmation checks the file's magic bytes against the declared type.
  - Every read checks ownership. The key locates a file; it does not grant access to it.
- **Payout numbers** are encrypted with AES-256-GCM. A keyed HMAC fingerprint recognises a resubmitted number, so it is updated rather than duplicated. Only the masked number is ever returned.
- **The catalogue** is seeded by the migration (`CatalogueSeed`), using ids derived from names. It matches the bundled Ghana config in GRID Pro, which matches areas by name.

```
src/Grid.Api/
  Common/          envelope, errors, validation, phone numbers
  Data/            DbContext, migrations, development seed
  Features/        Auth, Catalogue, Consumers, Kyc, Media, Properties, Providers, UtilityAccounts
  Integrations/    Sms (Log, Arkesel, Hubtel, router), Hubtel (name lookup)
```

Adding a migration:

```sh
dotnet ef migrations add <Name> -p src/Grid.Api -o Data/Migrations
```

## Not built yet

- **Ops review of provider applications.** Nothing moves an application past `Submitted` yet, and media reads are owner-only until reviewers exist.
- **Going online** (`PUT /provider-accounts/me/operating-status`, `providers.not_job_ready`).
- **Virus scanning and cleanup of abandoned uploads.**
- **Payments and utility purchases.**
- **SMS delivery-receipt webhooks.** Provider message ids are kept on `otp_challenges` so they can be added.
- **Ops endpoints**, including a KYC read.
