# Bookings, locations and dispatch

How GRID's services are priced, how a customer pays, how a request finds the nearest
professional, and what happens to it afterwards.

## Services and packages

GRID runs a managed marketplace: the customer buys a defined job at GRID's price and GRID assigns
a vetted professional. Two levels:

```
Category  (cleaning)                      what a provider is approved for; what dispatch matches on
  └ Package  (Standard clean · 2 bedrooms, GH₵ 250, ~3 h)   what a customer buys
```

| Category | Booking mode | Launched |
|---|---|---|
| `cleaning`, `laundry` | `FixedPrice` | Yes ("Cleaning & laundry") |
| `ac-servicing`, `pest-control` | `FixedPrice` | Yes ("Repairs & maintenance") |
| `plumbing`, `electrical` | `CallOutThenQuote` | No: seeded with a call-out package, hidden until the quote flow ships |

Packages live in `CatalogueSeed` (one place to change a price; add a migration). A package has a
`group` ("Standard clean"), a `name` ("2 bedrooms"), a `price`, a `durationMinutes`, an optional
`unitLabel` ("seat", "bag", "unit") and a `maxQuantity`. Packages in one group without a unit are
sizes of the same job, so a booking takes **one** of them; packages with a unit add up.

**Launch prices are proposals** to check with a handful of Accra providers before launch.

## Where things are

Three kinds of place, all in decimal degrees:

| Thing | Fields | Where it comes from |
|---|---|---|
| **Service area** | `latitude`, `longitude`, `radius_km` | Seeded in `CatalogueSeed`: a centre and a radius per town |
| **Home** (`properties`) | `latitude`, `longitude`, `service_area_id` | The customer's device, when it shares a location; otherwise the area name they chose |
| **Provider** (`provider_accounts`) | `base_latitude`, `base_longitude`, `travel_radius_km` | `PUT /provider-accounts/me/location`; otherwise the areas they picked in onboarding |

A home belongs to the area whose centre is nearest among the circles that contain it (with 2 km of
tolerance, since the circles are drawn tight). Without coordinates, the area name is matched
instead. A home in no area cannot be booked: `services.area_not_served`.

**The seeded coordinates are approximate** (to about a kilometre) and need checking against a
map before launch. Change them in `CatalogueSeed` and add a migration; ids come from names, so
they never change.

## Who is offered a request

A provider is eligible when all of these hold:

1. approved and **online**;
2. an active offering for the service (no price: GRID sets it), and the licence the service needs
   (`EpaPestControlLicence` for pest control, `EnergyCommissionCertificate` for electrical);
3. working that day and time (weekly rules, Africa/Accra);
4. no other visit within two hours of it (laundry never blocks);
5. **reachable**: they listed the request's area, **or** the job is within their travel radius.

Eligible providers are ranked by straight-line distance from their base to the job, then by how
few jobs they already have. Their base is the location their phone shared, or else the centre of
the nearest area they cover. Distance is great-circle, computed in the API; there are few enough
online providers that this needs no spatial index yet.

Dispatch starts only once the booking is **paid**. When the customer asked for someone they used
before (`preferredProviderAccountId`), the first round offers that provider alone, if they are
still eligible; then dispatch carries on as normal.

The nearest three are offered the request at once, for ten minutes. If nobody accepts, the next
three are tried. After four rounds with no taker the request becomes `Unfilled` and **the payment
is refunded**; the customer books again. While a request is still `Searching`, Ops can push the
next round now (`GET /ops/service-requests`, `POST /ops/service-requests/{id}/redispatch`). A
provider is offered a given request once.

## Price and payment

`ServicePricing` prices a booking from its package lines: unit price × quantity, summed. The
client's idea of the price is never used. The lines are copied onto the request
(`service_request_lines`) so later price changes don't touch existing bookings. An offer and its
job carry that total as `price`; GRID's fee (`Marketplace:FeePercent`, 15) and the provider's
`earnings` are fixed onto the job at acceptance.

The customer pays at booking with a Mobile Money prompt through Hubtel's Direct Receive Money,
the same rail as bill payments (`ServicePaymentService`, client reference `S{requestId:N}`):

```
POST /service-requests ──► AwaitingPayment ──paid──► Searching ──► Assigned ──► … ──► Completed
                                  │
                                  └─declined / timed out──► PaymentFailed ──POST /pay──► AwaitingPayment
```

- The callback or a status check settles a prompt; an unanswered prompt fails after
  `Marketplace:PaymentTimeoutMinutes` (15). A payment that lands after the booking was cancelled
  or failed is refunded (`LatePayment`).
- **Refunds are automatic** when the request goes `Unfilled` or the customer cancels before the
  provider sets off, in full, through Hubtel's refund API. Ops can refund by hand:
  `POST /ops/service-requests/{id}/refund`.
- GRID holds the money until the job is done. The provider's earning is `Pending` at the last job
  step, `Available` once the customer confirms (or `AutoConfirmHours` later), `Paid` on payout.
- `Marketplace:PaymentMode` is `Hubtel`, or `Simulated` in Development (paid at once, no prompt).
  The API refuses to start with `Simulated` in Production.

## Reviews

After a completed job the customer can rate it once (`POST /service-requests/{id}/review`,
1–5 stars and an optional comment). A provider's average `rating` and `reviews` count appear on
their summary and on the customer's booking.

## Ops

Phone numbers in `Ops:Operators` can use `/api/v1/ops`:

```
GET  /ops/provider-applications[?status=]            GET /ops/provider-applications/{id}
POST /ops/provider-applications/{id}/start-review | approve | request-information {note} | reject {note}
POST /ops/verification-documents/{id}/approve | reject {note}      GET /ops/media/{storageKey}
GET  /ops/service-requests[?status=]                 POST …/{id}/redispatch (Searching only) | cancel
POST /ops/service-requests/{id}/refund            refunds a paid booking that isn't under way
POST /ops/jobs/{id}/confirm-completion
POST /ops/provider-payouts   { providerAccountId, externalReference? }   records everything available as paid
GET  /ops/support-tickets[?status=]                  POST /ops/support-tickets/{id}/resolve {note}
```

## Configuration

| Key | Purpose |
|---|---|
| `Marketplace:Enabled` | Lets customers book. Off by default outside Development |
| `Marketplace:FeePercent`, `OffersPerRound`, `OfferMinutes`, `MaxRounds`, `RetryMinutes` | Fee and dispatch pacing |
| `Marketplace:DefaultTravelRadiusKm`, `VisitGapMinutes`, `MinLeadMinutes`, `MaxLeadDays`, `AutoConfirmHours` | Matching and booking rules |
| `Marketplace:PaymentMode`, `PaymentTimeoutMinutes` | `Hubtel` or `Simulated` (Development only), and how long a MoMo prompt may wait |
| `Marketplace:RunWorker`, `PollSeconds` | The worker that settles payments and refunds, lapses offers, sends rounds and auto-confirms jobs |
| `Ops:Operators` | Phone numbers allowed to use `/ops` |
| `Media:Provider` | `R2` in production, `Local` otherwise |
| `Media:R2:AccountId`, `AccessKeyId`, `SecretAccessKey`, `Bucket` | An R2 API token with Object Read & Write on one private bucket. `Endpoint` overrides the host for a jurisdiction bucket |
