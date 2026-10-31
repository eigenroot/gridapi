# Integrating a client with the GRID API

What every GRID client must implement to talk to `Grid.API`, and how the provider app maps its
onboarding flow onto endpoints. Live request/response schemas are at `/scalar/v1` on a running
server; this document is about the contract a client is held to.

Base path: `/api/v1`. Local dev host `http://localhost:8080` — from the **Android emulator that is
`http://10.0.2.2:8080`**, since `localhost` there is the emulator itself. Browsable reference at
`/scalar/v1`, OpenAPI JSON at `/openapi/v1.json`.

## The response envelope

Every response — success or failure — is wrapped:

```json
{ "success": true, "data": { }, "error": null, "traceId": "…", "timestamp": "2026-07-23T…Z" }
```

```json
{ "success": false, "data": null,
  "error": { "code": "jobs.invalid_transition", "message": "…", "detail": null, "fields": [] },
  "traceId": "…", "timestamp": "…" }
```

Two shapes a client must survive:

1. **The envelope with `data`** — the normal case.
2. **The envelope with no `data` key at all** — every void command (logout, delete…) and every
   error. Treating a missing `data` as an error will break those endpoints.

Malformed bodies (`request.malformed`), missing or expired tokens (`auth.unauthenticated`),
rate limits and unknown routes (`route.not_found`) all use the envelope too.

Branch on `error.code`, never on `error.message`. Codes are stable; messages are not.
HTTP status still reflects the class: `400/401/403/404/409/422/500`.

Other conventions: enums are **PascalCase strings** (`"Individual"`, `"InspectionThenQuote"`) —
except **job stage codes, which are camelCase** (`"onTheWay"`, `"workInProgress"`). Money is
decimal GHS. Timestamps are UTC ISO-8601. List endpoints return bare arrays inside `data`; only
the two Ops list endpoints paginate.

## Authentication

Passwordless phone + OTP, then JWT bearer. Both apps sign in the same way, and one phone number is
one person across both apps.

```
POST /auth/otp/request   { "phoneNumber": "0558151870" }
     → { phoneNumber, expiresInSeconds, resendAfterSeconds, developmentCode? }
POST /auth/otp/verify    { "phoneNumber": "…", "code": "123456", "fullName"? }
     → { accessToken, accessTokenExpiresAt, refreshToken, refreshTokenExpiresAt, isNewUser, user }
POST /auth/tokens/refresh { "refreshToken": "…" }   → same shape as verify
POST /auth/logout         { "refreshToken": "…" }
GET  /auth/me             → user
```

`user` is `{ id, phoneNumber, fullName, firstName, lastName, email, profileComplete, providerAccountId }`.

**Phone numbers** are accepted as `0558151870`, `558151870`, `+233 55 815 1870` or
`233558151870`. They are always returned in **`233XXXXXXXXX`** form, so compare against that.

**Completing sign-up.** When `user.profileComplete` is false, show the profile screen and send
`PUT /consumers/me { firstName, lastName?, email? }`. GRID Pro may instead pass `fullName` to
`otp/verify`; for a new person it is split into first and last name and completes the profile.

**Identify the app** with an `X-Grid-App: consumer` or `X-Grid-App: pro` header on every request.
It records which app a person first and last signed in from.

Send `Authorization: Bearer <accessToken>`. Access tokens last 30 minutes. A missing, invalid or
expired token returns **401** `auth.unauthenticated`, which is the client's cue to refresh.

**Refresh tokens are single-use and rotate**: refreshing revokes the old token and issues a new
pair. A client that fires several refreshes concurrently will revoke its own session, so refresh
must be collapsed behind a single in-flight future and the failed requests replayed after it.

Store tokens in platform secure storage. `shared_preferences` is not sufficient.

`user.providerAccountId` is how a client knows the signed-in user is a provider — `/auth/otp/verify`
always creates users with the `Customer` role, and `ProviderOwner` is granted later by
`POST /provider-accounts`. There is no provider-specific signup endpoint.

In Development (`Otp:DevelopmentMode=true`) every code is `123456` and the response echoes it as
`developmentCode`. In production, codes go out over **Arkesel or Hubtel** SMS, whichever the server
is configured for. The code is GRID's own, so it doesn't matter to the client which one delivered it.

**Resending** is just another `otp/request`, which retires the previous code. Limits:

- 60 seconds between requests (`resendAfterSeconds`) and 5 per phone per hour. Over either limit,
  the response is **429** `auth.otp_rate_limited` with `detail.retryAfterSeconds`.
- 5 wrong guesses per code. Wrong guesses return `auth.otp_invalid` with
  `detail.attemptsRemaining`; after the fifth, `auth.otp_attempts_exceeded`.
- A code lasts 5 minutes; after that, or once used, it returns `auth.otp_expired`.
- If SMS delivery fails, the response is **503** `sms.unavailable` and no cooldown is spent.

## Consumer profile, homes and saved accounts

```
GET  /consumers/me
PUT  /consumers/me                    { firstName, lastName?, email? }        full replace
PUT  /consumers/me/default-property   { propertyId }

GET  /properties                      POST /properties
GET  /properties/{id}                 PUT  /properties/{id}    DELETE /properties/{id}  (archives)
     { nickname, kind: House|Apartment|Office, area, city, address, ghanaPostGps? }
     → … + isPrimary

GET  /utility-accounts?type=Ecg       POST /utility-accounts
GET  /utility-accounts/{id}           PUT  /utility-accounts/{id}  DELETE /utility-accounts/{id}  (archives)
     { type, accountNumber, nickname, propertyId?, isDefault? }
```

- **Homes.** The first home becomes the default (`isPrimary`). Archiving the default hands it to the
  most recently added remaining home, and unlinks saved accounts from the archived one.
- **Saved accounts.**
  - `type` is one of `Ecg`, `Water`, `Dstv`, `Gotv`, `Startimes`, `Airtime`, `DataBundle`, `Internet`.
  - The server owns the digit-length rules. Airtime and data numbers come back in `233…` form.
  - The first account of a utility becomes its default. Sending `isDefault: true` moves the default.
  - `lastPaidAt`, `lastAmount` and `dueDate` stay null until payments exist.

## Media upload

Three steps, and the middle one is easy to miss:

```
POST /media/upload-requests  { purpose, fileName, contentType, sizeBytes }
     → { storageKey, uploadUrl, headers, expiresAt }
PUT  <uploadUrl>             raw bytes, Content-Type set, Authorization required
                             (uploadUrl is absolute: …/api/v1/media/uploads/{storageKey})
POST /media/uploads/confirm  { storageKey }   → checks the bytes match the declared type
GET  /api/v1/media/{key}     authenticated download
```

- **Purpose** is `portfolio`, `certificates`, `identity` or `profilePhoto`, in any case. It decides what the upload can later be attached to:
  - portfolio items take `portfolio`;
  - Ghana Card, selfie and profile-photo documents take `identity` or `profilePhoto`;
  - certificates and business registration take `certificates`.
- **The upload link** lasts 15 minutes (`media.upload_expired`). The PUT can be repeated until the upload is confirmed.
- **Each confirmed upload attaches once** (`media.already_attached`).

Allowed types: `image/jpeg`, `image/png`, `image/webp`, `image/heic`, `application/pdf`,
`video/mp4`. Maximum 25 MB, enforced on the bytes actually streamed. The streamed size is also capped at the declared `sizeBytes`.

Serving is **authenticated** — these objects include identity documents, so the unguessable key is
a locator, not an access grant. Clients rendering a stored image must send the bearer token, and
should build the URL from their own configured base URL rather than trusting `publicUrl`, which is
generated from server-side configuration.

## Provider onboarding → endpoints

The account must exist before anything else. The first call creates it, together with the draft
application, and from then on `user.providerAccountId` is set. There are no roles in the token.
`GET /provider-accounts/me` returns **404 `providers.account_not_found`** until the account exists.

The catalogue reads `GET /service-categories` and `GET /service-areas` need no token. Categories are
hierarchical: offerings use a top-level id (`cleaning`, `laundry`), and the children are sub-services.

| Step | Call | Notes |
|---|---|---|
| Provider type | `POST /provider-accounts` | `accountType` `Individual`/`Business`, `displayName` required |
| Details | `PUT /provider-accounts/me` | **Full replace** — resend every field. Carries `deliveryMode` (`TravelsToCustomer`/`FixedPremises`/`Both`) and `premisesAddress` |
| Services | `POST`/`PUT /provider-offerings` | `serviceCategoryId` from the catalogue |
| Experience | `PUT /provider-applications/me/experience` | |
| Service area | `POST /provider-service-areas` | One call per area; `GET`/`DELETE` to reconcile |
| Availability | `PUT /provider-availability/rules` | **Full replace** of the weekly pattern |
| Pricing | same offering as Services | `InspectionThenQuote`+`inspectionFee`, `PerUnit`+`unitPrice`, `StartingFrom`+`startingPrice` |
| Payout | `POST /payout-methods` | `makeDefault: true`; only `maskedAccountNumber` is ever returned |
| Portfolio | upload → `POST /provider-portfolio` | |
| Identity | upload → `POST /provider-verification-documents` | `GhanaCard`, `GhanaCardBack`, `Selfie`, `ProfilePhoto`, … |
| Standards | `POST /provider-standards/current/accept` | |
| Review | `GET /provider-applications/me/readiness` | |
| Submit | `POST /provider-applications/me/submit` | Empty body |
| Status | `GET /provider-applications/me` | |

**Readiness is server-authoritative.** Client completion flags are ignored; render
`missingRequirements[].message` so what the provider sees matches what submission accepts.
Application readiness needs:

- basic information;
- at least one offering, priced for its pricing mode;
- at least one service area;
- at least one available day;
- both sides of the Ghana Card;
- a profile photo (a `Selfie` counts);
- acceptance of the current standards.

Prices are optional when an offering is saved, and a missing one shows as `pricing.required`.
Submitting while anything is missing returns **422 `providers.application_incomplete`**, with
`detail.missingRequirements`. Repeating a submit that already went through returns the submitted
application.

Re-posting a payout method with the same type and number updates it instead of adding a duplicate.
Adding a service area that is already listed returns the existing row.

*Going online* additionally needs a default payout method, and fails with `providers.not_job_ready`.
It is not built yet.

**Verification-critical fields lock** once the application is `Submitted`, `UnderReview` or
`Approved` — edits then return `409 providers.locked`. They unlock again on
`MoreInformationRequired`.

Status transitions, mirrored from the application onto the account:

```
Draft → Submitted → UnderReview → Approved
                          ↘ MoreInformationRequired → (edit) → Submitted
                          ↘ Rejected
```

There is no push channel, so clients poll for the decision.

## Error codes worth handling

| Code | Meaning |
|---|---|
| `auth.unauthenticated` | Not signed in, or the access token expired (**401**) — refresh, then retry |
| `auth.invalid_phone` | Not a Ghana mobile number |
| `auth.otp_invalid` / `auth.otp_expired` / `auth.otp_attempts_exceeded` | See Authentication |
| `auth.otp_rate_limited` | Too many code requests; wait `detail.retryAfterSeconds` |
| `auth.refresh_invalid` | Refresh token unknown, expired, revoked or reused — sign in again |
| `sms.unavailable` | No SMS provider accepted the code; try again shortly |
| `consumers.suspended` | Account suspended by GRID |
| `validation.failed` | `error.fields[]` lists each `{ field, message }` |
| `properties.not_found` / `utility_accounts.not_found` | Missing, archived or someone else's |
| `utility_accounts.duplicate` | That number is already saved for that utility |
| `utility_accounts.invalid_number` | Wrong length or characters for that utility |
| `providers.account_not_found` | Caller owns no provider account |
| `providers.account_exists` | `POST /provider-accounts` when the caller already has one |
| `providers.locked` | Edit attempted while under review |
| `providers.application_incomplete` | Submit before readiness is met (**422**); see `detail.missingRequirements` |
| `providers.application_not_submittable` | Submit from `UnderReview`, `Approved` or `Rejected` |
| `providers.offering_exists` | A second offering for the same service |
| `catalogue.category_not_found` / `catalogue.area_not_found` | Unknown id, or a sub-service where a top-level service is needed |
| `providers.not_job_ready` | Going online before payout/areas/availability exist |
| `providers.premises_address_required` | Fixed premises chosen without an address |
| `jobs.offer_already_accepted` | Another provider took the offer first |
| `jobs.invalid_transition` | Stage move not allowed from here |
| `quotes.customer_approval_required` | Quote-gated stage attempted before approval |
| `quotes.self_approval_forbidden` | A provider cannot approve their own quote |
| `media.unsupported_type` / `media.too_large` / `media.not_uploaded` | Upload rejected |
| `media.upload_expired` / `media.already_confirmed` / `media.already_attached` / `media.wrong_purpose` | Upload link or attachment misuse |

## Jobs

Job stages are server-controlled and strictly linear — there is no generic status update, only
intent commands (`start-journey`, `arrive`, `start-inspection`, `start-work`, `complete-work`,
`confirm-completion`, `cancel`, …). **Gate action buttons on `JobResponse.allowedNextStages`**
rather than assuming a sequence.

Two practical constraints today: only the **Repair** workflow is fully drivable from a client —
the other four have scheduling stages no endpoint can enter — and `confirm-completion` rejects the
provider, since only the customer or Ops may close a job.

Offers carry no customer PII before acceptance: `generalArea` only, and `distanceKm` is always
null because there is no geo matching yet.
