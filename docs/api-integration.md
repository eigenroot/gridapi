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
     { type, accountNumber, nickname, propertyId?, isDefault?, network?, ecgKind? }
```

- **Homes.** The first home becomes the default (`isPrimary`). Archiving the default hands it to the
  most recently added remaining home, and unlinks saved accounts from the archived one.
- **Saved accounts.**
  - `type` is one of `Ecg`, `Water`, `Dstv`, `Gotv`, `Startimes`, `Airtime`, `DataBundle`, `Internet`.
  - The server owns identifier rules. ECG accepts alphanumeric meter identifiers and nine-digit
    account numbers. Airtime and data numbers come back in `233…` form.
  - The first account of a utility becomes its default. Sending `isDefault: true` moves the default.
  - `network` (`Mtn`, `Telecel`, `AirtelTigo`) is only for `Airtime` and `DataBundle`. It defaults to
    the number's original network; send it to override a ported number.
  - `ecgKind` (`Prepaid`, `Postpaid`) is only for `Ecg` and defaults to `Prepaid`.
  - `lastPaidAt` and `lastAmount` come from the newest delivered bill paid with that saved account.
    `dueDate` is always null, because no biller reports one.

## Bill payments

Bills are paid through Hubtel. The full contract, states and operations guide are in
[bill-payments.md](bill-payments.md).

```
GET  /bill-products                    catalogue: code, lookup, amountRule, enabled
GET  /payment-channels                 { channels[], suggested, payerPhone }
POST /bill-lookups                     { productCode, accountNumber?, phoneNumber?, meterNumber? }   stateless
POST /orders        Idempotency-Key    { productCode, amount, accountNumber?, phoneNumber?, meterNumber?, bundle?, channel?, savedAccountId?, propertyId? }
GET  /orders/{id}                      poll until status is final
GET  /orders?limit=&before=            GET /orders/{id}/receipt
```

- **Placing an order prompts the signed-in number.** Show "Approve on your phone" while
  `nextAction` is present, and poll `GET /orders/{id}` with backoff.
- **`status`** is `AwaitingPayment`, `PaymentFailed`, `Processing`, `Completed`, `Refunding` or
  `Refunded`. Only `Completed` means the bill was paid. `PaymentFailed` is final: to retry, place a
  new order with a new key.
- **Data bundles** come from the lookup. Send the bundle's `value` as `bundle`, and its exact price
  as `amount`.
- **Hubtel's charge** comes on top of `total`. The order returns the real `charges` and
  `amountCharged` as soon as the prompt is sent.

## Media upload

Three steps, and the middle one is easy to miss:

```
POST /media/upload-requests  { purpose, fileName, contentType, sizeBytes }
     → { storageKey, uploadUrl, headers, expiresAt }
PUT  <uploadUrl>             raw bytes, with the Content-Type from `headers`
POST /media/uploads/confirm  { storageKey }   → checks the bytes match the declared type
GET  /api/v1/media/{key}     authenticated download
```

- **Where `uploadUrl` points depends on the server.**
  - In production files live in Cloudflare R2, and the link goes **straight to the bucket**. It is
    signed for the declared content type and exact size. Send no `Authorization` header: storage
    rejects a request that carries both a signature and a bearer token.
  - In development the link is the API's own `…/api/v1/media/uploads/{storageKey}`, which **needs**
    the bearer token.
  - A client decides by host: send the token only when the link's host is the API's.
- **Purpose** is `portfolio`, `certificates`, `identity` or `profilePhoto`, in any case. It decides what the upload can later be attached to:
  - portfolio items take `portfolio`;
  - Ghana Card, selfie and profile-photo documents take `identity` or `profilePhoto`;
  - certificates and business registration take `certificates`.
- **The upload link** lasts 15 minutes. The PUT can be repeated until the upload is confirmed.
- **Each confirmed upload attaches once** (`media.already_attached`).

Allowed types: `image/jpeg`, `image/png`, `image/webp`, `image/heic`, `application/pdf`,
`video/mp4`. Maximum 25 MB. `sizeBytes` must be the file's real size.

Serving is **authenticated** and always goes through the API — these objects include identity
documents, so the key is a locator, not an access grant, and the bucket has no public access.
Clients rendering a stored image must send the bearer token and build the URL from their own
configured base URL.

## Provider onboarding → endpoints

The account must exist before anything else. The first call creates it, together with the draft
application, and from then on `user.providerAccountId` is set. There are no roles in the token.
`GET /provider-accounts/me` returns **404 `providers.account_not_found`** until the account exists.

The catalogue reads `GET /service-categories` and `GET /service-areas` need no token. Categories are
hierarchical: offerings use a top-level id (`cleaning`, `laundry`, `ac-servicing`,
`pest-control`), and the children are skills. Each category carries `bookingMode`, `section` and
`isLaunched`; offer only launched ones. GRID sets prices, so GRID Pro reads
`GET /service-catalogue` for the packages and `feePercent` to show what a provider earns.

| Step | Call | Notes |
|---|---|---|
| Provider type | `POST /provider-accounts` | `accountType` `Individual`/`Business`, `displayName` required |
| Details | `PUT /provider-accounts/me` | **Full replace** — resend every field. Carries `deliveryMode` (`TravelsToCustomer`/`FixedPremises`/`Both`) and `premisesAddress` |
| Services | `POST`/`PUT /provider-offerings` | One per chosen service, `serviceCategoryId` from the catalogue; `isActive: false` to drop one. No prices |
| Experience | `PUT /provider-applications/me/experience` | |
| Service area | `POST /provider-service-areas` | One call per area; `GET`/`DELETE` to reconcile |
| Availability | `PUT /provider-availability/rules` | **Full replace** of the weekly pattern |
| What you earn | — | Read-only: package `price` × (100 − `feePercent`)% |
| Payout | `POST /payout-methods` | `makeDefault: true`; only `maskedAccountNumber` is ever returned |
| Portfolio | upload → `POST /provider-portfolio` | |
| Identity | upload → `POST /provider-verification-documents` | `GhanaCard`, `GhanaCardBack`, `Selfie`, `ProfilePhoto`; `EpaPestControlLicence` for pest control, `EnergyCommissionCertificate` for electrical |
| Standards | `POST /provider-standards/current/accept` | |
| Review | `GET /provider-applications/me/readiness` | |
| Submit | `POST /provider-applications/me/submit` | Empty body |
| Status | `GET /provider-applications/me` | |

**Readiness is server-authoritative.** Client completion flags are ignored; render
`missingRequirements[].message` so what the provider sees matches what submission accepts.
Application readiness needs:

- basic information;
- at least one active offering;
- the licence each offered service needs (`licence.epa_required`, `licence.energy_commission_required`);
- at least one service area;
- at least one available day;
- both sides of the Ghana Card;
- a profile photo (a `Selfie` counts);
- acceptance of the current standards.

Offering price fields are still accepted but ignored: every job is priced from GRID's packages.
Submitting while anything is missing returns **422 `providers.application_incomplete`**, with
`detail.missingRequirements`. Repeating a submit that already went through returns the submitted
application.

Re-posting a payout method with the same type and number updates it instead of adding a duplicate.
Adding a service area that is already listed returns the existing row.

After approval the provider can still pause or resume a service
(`PUT /provider-offerings/{id}`, same service only), their areas, their hours, and their
description (`PUT /provider-accounts/me/profile { description }`).

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
| `jobs.offer_already_accepted` / `jobs.offer_expired` | Another provider took it first / it ran out or was declined |
| `services.disabled` | Booking is switched off (**503**) |
| `services.area_not_served` | The home is outside every service area (**422**) |
| `services.request_not_cancellable` | The provider has already set off |
| `services.not_launched` | That service can't be booked yet (**422**) |
| `services.payment_not_retryable` | `/pay` on a booking that isn't `PaymentFailed` (**409**) |
| `catalogue.package_not_found` | A package was retired; reload the catalogue (**404**) |
| `reviews.not_completed`, `reviews.already_reviewed` | Rating too early, or twice (**409**) |
| `licence.epa_required`, `licence.energy_commission_required` | Readiness: a licence is missing for an offered service |
| `ops.forbidden` | Not one of `Ops:Operators` |
| `jobs.invalid_transition` | Stage move not allowed from here |
| `media.unsupported_type` / `media.too_large` / `media.not_uploaded` | Upload rejected |
| `media.upload_expired` / `media.already_confirmed` / `media.already_attached` / `media.wrong_purpose` | Upload link or attachment misuse |
| `payments.disabled` / `bills.product_paused` | Bill payments, or this product, are switched off (**503**) |
| `bills.account_not_found` / `bills.no_meters` / `bills.no_bundles` | The biller doesn't know that account, phone or number |
| `bills.meter_selection_required` | Several ECG meters on that phone (**409**); `detail` lists them, so send `meterNumber` |
| `bills.email_required` | Ghana Water needs an email on the profile |
| `bills.saved_account_mismatch` | `savedAccountId` isn't for this bill |
| `payments.idempotency_required` / `payments.idempotency_conflict` | Missing `Idempotency-Key`, or the same key reused for a different order |
| `payments.receipt_not_ready` | The order hasn't been paid yet |
| `payments.provider_unavailable` | Hubtel didn't answer; try again shortly |
| `payments.rate_limited` | Too many orders or lookups in a minute |

## The provider's working day

```
GET  /provider-accounts/me/summary            name, phone, services, areas, payout method, completedJobs,
                                              rating, reviews, canGoOnline, missingToGoOnline[]
PUT  /provider-accounts/me/operating-status   { operatingStatus: Online|Offline } → summary
PUT  /provider-accounts/me/location           { latitude?, longitude?, travelRadiusKm? } → summary

GET  /job-offers?status=Pending               open requests, soonest to expire first
GET  /job-offers/{id}
POST /job-offers/{id}/accept                  → the job
POST /job-offers/{id}/decline                 { reason? }

GET  /jobs?status=active|completed            GET /jobs/{id}
POST /jobs/{id}/{nextCommand}                 → the job
POST /jobs/{id}/cancel                        { reason }   only before setting off

GET  /provider-earnings/summary               availableAmount, pendingAmount, paidAmount, last7Days, last30Days
GET  /provider-earnings                       GET /provider-payouts
POST /support-tickets                         { category?, subject, details, jobId? }   GET /support-tickets
```

- **Going online** needs an approved application, an active service, an area, a working day
  and a default payout method. Otherwise **409 `providers.not_job_ready`** with
  `detail.missingRequirements`. The same list is in the summary, so the app can say why up front.
- **Offers carry no customer details**: `generalArea`, `distanceKm`, the schedule, the `title` and
  `scope` of what was booked ("3 × General service"), and `price`, `fee` and `earnings`. The name, phone and address arrive on the job. `customerPhone` is null once
  the job is no longer live.
- **Several providers hold the same request.** The first to accept gets the job; the rest get
  **409 `jobs.offer_already_accepted`**. An offer that ran out or was declined answers
  `jobs.offer_expired`.
- **Jobs move one step at a time.** `nextCommand` on the job is the only step GRID will take:
  `start-journey → arrive → start-work → complete-work` for cleaning, AC servicing and pest
  control, and
  `confirm-pickup → start-processing → mark-ready → start-delivery → confirm-delivery` for laundry.
  Anything else is **409 `jobs.invalid_transition`**. Repeating the step just taken is harmless.
- **`stage`** is camelCase: `accepted`, `onTheWay`, `arrived`, `inProgress`, `pickedUp`,
  `processing`, `readyForDelivery`, `outForDelivery`, `awaitingConfirmation`, `completed`,
  `cancelled`.
- **Money.** The last step creates a `Pending` earning. It becomes `Available` when the customer
  confirms (or 24 hours later), and `Paid` when GRID records a payout.
- There is no push channel. GRID Pro re-reads offers every 30 seconds while it is open.

## Booking a service (GRID Home)

```
GET  /service-catalogue                               sections → services → packages at GRID's prices, feePercent
GET  /service-areas/locate?latitude=&longitude=       { served, area, distanceKm }
GET  /service-requests/availability?serviceCategoryId=&propertyId=&date=
                                                      { served, area, providersNearby, slots[{ startsAt, available, providers }] }
POST /service-requests       Idempotency-Key: <uuid>
                             { lines: [{ packageId, quantity }], propertyId, scheduledAt, urgency?, items?, notes?,
                               channel?, preferredProviderAccountId? }
GET  /service-requests       GET /service-requests/{id}
POST /service-requests/{id}/pay                       { channel? }   a new MoMo prompt after PaymentFailed
POST /service-requests/{id}/cancel                    { reason? }    until the provider sets off; refunds in full
POST /service-requests/{id}/confirm-completion        releases the provider's earning
POST /service-requests/{id}/review                    { rating 1–5, comment? }   once, after completion
```

- **The server prices the booking.** Send package ids and quantities only; `total` and `lines`
  come back. One service per booking, and one size per group of alternatives (e.g. one bedroom
  count of Standard clean).
- **Paying.** Creating a booking sends the MoMo prompt to the signed-in phone (or `channel`) and
  returns `status: AwaitingPayment` with `payment.nextAction: ApproveOnPhone`. Poll
  `GET /service-requests/{id}` until it is `Searching` (paid) or `PaymentFailed`. A retry with the
  same `Idempotency-Key` returns the same booking and never sends a second prompt.
- **`payment.status`**: `AwaitingApproval`, `Paid`, `Failed`, `Refunding`, `Refunded`,
  `RefundFailed`.
- **`status`**: `AwaitingPayment`, `PaymentFailed`, `Searching`, `Assigned`, `InProgress`,
  `Completed`, `Cancelled`, `Unfilled`. `canCancel` says whether cancel is allowed now;
  `jobStage` follows the provider; `provider` (name, phone, completed jobs, rating) appears once
  someone accepts; `review` once rated.
- **Book again** by sending `preferredProviderAccountId` from an earlier booking's `provider.id`.

`/properties` accepts optional `latitude` and `longitude` and returns the `serviceAreaId` the home
falls in. How requests reach providers, and how payments and refunds work, is in
[marketplace.md](marketplace.md).
