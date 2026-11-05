# Bill payments

GRID Home pays bills through Hubtel: ECG prepaid and postpaid, Ghana Water, DStv, GOtv,
StarTimes, MTN/Telecel/AT data and airtime, and Telecel Broadband. Payments are **off** until
`Payments:Enabled=true`.

## How money moves

```
Customer MoMo ──Receive Money──▶ Collection account ──sweep──▶ Disbursement float ──Commission Services──▶ biller
               ◀──────────────────── Refund API, if the bill definitively fails
```

1. **Order.** Placing an order prompts the customer's own number. Hubtel adds its charge on top.
2. **Bill.** Once the payment is confirmed, the worker pays the bill from the float.
3. **Refund.** If the bill definitively fails, the money goes back through Hubtel's Refund API.
4. **Float.** When the float runs low, the worker tops it up from the Collection account.

**One payment attempt per order.** If the customer declines, the order ends as `PaymentFailed` and
the app places a new one.

## Tables

| Table | One row per | Key columns |
|---|---|---|
| `orders` | Purchase, and its mobile-money payment | `status` (what the customer sees), `payment_state`, the frozen price, `client_reference`, `hubtel_order_id`, `needs_review` |
| `bills` | Order, at most one | `state`, `attempts`, `client_reference` (renewed on a safe retry), `external_reference` (the biller's receipt) |
| `refunds` | Order, at most one | `state` (`Failed` means finance must act), `method` (`Hubtel` or `Manual`), `error_code` |
| `float_transfers` | Sweep from Collection to the float | `state`; at most one open at a time |
| `order_events` | Thing that happened | Append-only, enforced by a database trigger |

**Order `status`** is `AwaitingPayment` → `Processing` → `Completed`. It can also end at
`PaymentFailed`, or go `Refunding` → `Refunded`.

**Every money leg has a `TxnState`** (`Created`, `Submitting`, `Pending`, `Succeeded`, `Failed`,
`Unknown`). This covers an order's payment, a bill, a refund and a transfer.

**The tables are also the work queue.** Every row has `next_check_at` and `lease_until`, and the
worker picks up rows whose check is due.

## Customer API

All routes are under `/api/v1`, need a bearer token, and use the GRID envelope. Money is
`{ "amount": "100.00", "currency": "GHS" }`.

| Call | What it does |
|---|---|
| `GET /bill-products` | Product codes, the lookup each needs, amount rules, and whether each is enabled. |
| `GET /payment-channels` | `mtn-gh`, `vodafone-gh`, `tigo-gh`, plus the one suggested for your number. |
| `POST /bill-lookups` | `{ productCode, accountNumber?, phoneNumber?, meterNumber? }` → name, `nameConfirmed`, `amountDue`, `bundles[]`, `amountRule`, `chargePercent`. Stores nothing. |
| `POST /orders` + `Idempotency-Key` | `{ productCode, amount, accountNumber?, phoneNumber?, meterNumber?, bundle?, channel?, savedAccountId?, propertyId? }`. Sends the MoMo prompt and returns the order with `nextAction: ApproveOnPhone` and Hubtel's actual charge. |
| `GET /orders/{id}` | Poll until `status` is final. |
| `GET /orders?limit=&before=` | Your history, newest first. |
| `GET /orders/{id}/receipt` | Available once paid. The bill section appears when it's delivered. |

What each field means:

- **`accountNumber`** is the meter, smartcard, landline or phone being paid for.
- **`phoneNumber`** is the phone an ECG or water account is linked to. It defaults to yours.
- **`bundle`** is required for data. It must be one returned by the lookup, at exactly its price.
- **The payer is always the signed-in number.** Hubtel requires this, to stop unsolicited prompts.

ECG meter lookups have two special cases:

- **Several meters on the phone.** The lookup answers 409 `bills.meter_selection_required` with the
  list. Send `meterNumber` to pick one.
- **A meter not linked to the phone.** It's accepted with `nameConfirmed: false`, because Hubtel
  links it when paying. The app should ask the customer to double-check the number.

`POST /orders` repeats the lookup on the server. That gives a fresh Ghana Water `sessionId`, and
confirms the bundle and its price are still on offer.

## Safety rules

- **Nothing is sent twice.** Every call to Hubtel is committed as `Submitting` first. A row found in
  `Submitting`, `Pending` or `Unknown` is only ever status-checked, never sent again.
  - `0005` and transport errors stay `Unknown`.
  - After `ReviewAfterHours` (24), an unresolved row flags the order for review and is checked
    daily.
- **Idempotency.** The same `Idempotency-Key` with the same body returns the same order, with no
  second prompt. A different body gets a 409.
- **Callbacks** arrive at `POST /api/v1/hubtel/callbacks/{payment|bill|refund|transfer}/{id}/{signature}`.
  - The signature is `HMAC-SHA256(CallbackSecret, "{kind}:{id}")`, and the body's reference must
    match the row.
  - A callback is applied straight away under the order's row lock. Repeats are harmless.
  - The source address is checked against `18.202.122.131`. Mismatches are logged, and refused once
    `EnforceCallbackIp=true`.
  - Request-URL logging stays at Warning, so signatures never reach the logs.
- **Status checks** start `StatusCheckDelaySeconds` (300) after a call, as Hubtel asks, then back
  off from 30s to hourly.
  - A payment still `Unpaid` after 15 minutes counts as declined.
  - A bill still `Unpaid` after 60 minutes counts as failed.
- **Safe bill retries.** A bill Hubtel rejected before processing is retried every 2 minutes, up to
  10 times, with a new reference. That covers `4075` (no float, which also triggers a sweep straight
  away), `4101`/`4103` (credentials) and a bare 403. Anything else that fails refunds the customer.
- **Payments that don't match.**
  - A payment confirmed for a different amount is refunded, and the order is flagged.
  - A payment approved after the order was declined is refunded (`LatePayment`).
- **No refund while the bill might still succeed.** A refund is sent only when:
  - Hubtel knows the order,
  - it is at least GHS 1 and the payment is under 45 days old, and
  - the bill has definitively failed.

  Otherwise the refund waits for finance (`state = Failed` with an `error_code`). If Hubtel delivers
  the bill after all before the refund goes out, the refund is cancelled and the order is flagged.

## Finance

Routes under `/api/v1/ops/finance` are open to the phone numbers listed in `Payments:FinanceOperators`.

| Call | What it does |
|---|---|
| `GET /summary` | Collected, delivered, refunds open and done, review count, unresolved and overdue counts. |
| `GET /float` | Live Collection and float balances, the open sweep and the last one. |
| `GET /orders?status=&needsReview=` | Recent orders. |
| `GET /orders/{id}` | Full detail: payer, Hubtel references, the bill, the refund and every event. |
| `POST /orders/{id}/resolve-review` | `{ reason }`. Clears the flag; it never changes money. |
| `GET /refunds?state=` | Refunds; `Failed` means waiting for finance. |
| `POST /refunds/{id}/retry` | `{ reason }`. Sends a finance-held refund through Hubtel again. |
| `POST /refunds/{id}/complete` | `{ reason, externalReference, evidence }`. Records money returned by hand. Refused while the bill is unresolved or the order is flagged. |

## Configuration (`Payments`)

Secrets belong only in `/opt/grid/.env`. See `deploy/.env.example`.

| Key | Default | Purpose |
|---|---|---|
| `Enabled` | `false` | Lets customers place orders |
| `RunWorker` | `false` | Runs the worker in this process |
| `PollSeconds` | `2` | How often the worker looks for due rows |
| `CallbackBaseUrl` | | `https://grid-api.eigenroot.com` |
| `FeeFlat`, `FeePercent` | `0` | GRID's own fee |
| `ChargePercent` | `0` | Hint shown for Hubtel's charge |
| `MinAmount`, `MaxAmount` | `1`, `1000` | Airtime is also capped at 100 |
| `DisabledProducts` | | Product codes to pause |
| `StatusCheckDelaySeconds` | `300` | |
| `PaymentUnpaidFailMinutes`, `BillUnpaidFailMinutes` | `15`, `60` | |
| `BillRetrySeconds`, `MaxBillAttempts` | `120`, `10` | |
| `ReviewAfterHours` | `24` | |
| `RefundMinimum`, `RefundWindowDays` | `1`, `45` | |
| `Float:AutoSweep`, `LowWatermark`, `Target`, `CollectionReserve`, `MinTransfer`, `CheckMinutes` | `true`, `500`, `2000`, `200`, `1`, `10` | |
| `FinanceOperators` | | Phone numbers |
| `Hubtel:ApiId`, `ApiKey` | | Programmable API key with receive-money and commission-services scopes |
| `Hubtel:CollectionAccountNumber`, `DisbursementAccountNumber` | | Both merchant accounts |
| `Hubtel:CallbackSecret` | | At least 32 characters |
| `Hubtel:CallbackIps`, `EnforceCallbackIp` | `18.202.122.131`, `false` | |
| `Hubtel:FallbackReceiptEmail` | | Ghana Water needs an email |
| `Hubtel:TimeoutSeconds` | `20` | |
| `Hubtel:*BaseUrl`, `ServiceIds` | Hubtel's | |

**Live calls only work from the Droplet.** Hubtel only answers whitelisted server IPs, so only the
Droplet (`64.227.34.236`) can call it. Local runs and CI use
`tests/Grid.Api.Tests/Support/HubtelSimulator.cs`.

## Going live

1. **Configure.** Put the `Payments__Hubtel__*` keys and `Payments__FinanceOperators__0` in
   `/opt/grid/.env`.
2. **Start a single-product pilot.** Set `Payments__Enabled=true` and `Payments__RunWorker=true`.
   Disable every product except `MtnAirtime` with `Payments__DisabledProducts__0..11`. Redeploy;
   the release applies the migration.
3. **Buy GHS 1 of airtime** from a test number. Check the callback, `GET /orders/{id}`, the receipt
   and `/ops/finance/summary`.
4. **Enable the rest one at a time:** ECG, data, TV, water, broadband.
5. **Exercise the edges once:**
   - Force a refund with an invalid meter after paying, and confirm the money comes back.
   - Lower `Float:LowWatermark` and confirm a sweep shows in `/ops/finance/float`.
6. **Confirm with Hubtel:**
   - Is the account set so the customer bears the charge?
   - Do status checks cover bill payments?
   - What does Telecel Broadband expect in `bundle`? We send the `account` from its lookup.
   - How does the ECG postpaid lookup behave?
   - How long does a Ghana Water `sessionId` last?
   - Is the callback IP still `18.202.122.131`? Then set `EnforceCallbackIp=true`.

**Switching off.** `Enabled=false` stops new orders, and pauses bills that haven't been sent. Keep
the worker running while existing orders settle.
