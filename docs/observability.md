# Observability

The API sends traces, metrics and logs to Grafana Cloud with OpenTelemetry, using
Grafana's `.NET` distribution (`Grafana.OpenTelemetry.Base`). Setup is in
`src/Grid.Api/Common/Telemetry.cs`. Nothing is sent unless
`OTEL_EXPORTER_OTLP_ENDPOINT` is set.

## Turning it on

1. Create a free stack at grafana.com, in an EU region.
2. In the stack, open **Connections > OpenTelemetry (OTLP)** and generate a token.
3. Add the three values it shows to `/opt/grid/.env`:

   ```
   OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf
   OTEL_EXPORTER_OTLP_ENDPOINT=https://otlp-gateway-<region>.grafana.net/otlp
   OTEL_EXPORTER_OTLP_HEADERS=Authorization=Basic%20<token>
   ```

4. Recreate the API container, then look for `{service_name="grid-api"}` in **Explore > Loki**.

## What is sent

| Signal | Contents |
|---|---|
| Traces | Incoming requests and outgoing HTTP calls, including every Hubtel call |
| Metrics | Request rates and durations, .NET runtime and process stats |
| Logs | Everything at the configured log level, including one line per order event |

Each order event logs `Order event {kind} for {orderId}: {detail}`. It is a Warning when
the detail holds a Hubtel code such as `hubtel.4075`.

## What is kept out

- **Callback signatures.** `url.path` on callback requests is replaced with the route prefix.
- **Query strings.** OpenTelemetry redacts their values, which covers phone numbers on lookups.
- **`/health` requests.** They are not traced.
- **Database queries.** They are not traced, because the payment worker polls every few seconds.

## Volume

Every remaining request is traced, which is fine at current traffic. To sample when traffic grows:

```
OTEL_TRACES_SAMPLER=parentbased_traceidratio
OTEL_TRACES_SAMPLER_ARG=0.25
```

This decides when a request starts, so it can't keep only the failures. Keeping every
failed request would need a collector such as Alloy in front of Grafana.

## Alerts to create

Create these under **Alerts & IRM > Alert rules**, on the Loki data source, each over a
5-minute window unless noted.

| Alert | Query | Fires when |
|---|---|---|
| Float is empty | `sum(count_over_time({service_name="grid-api"} \|= "hubtel.4075" [5m]))` | above 0 |
| Hubtel refused our keys or IP | `sum(count_over_time({service_name="grid-api"} \|~ "hubtel\\.(forbidden\|4101\|4103)" [5m]))` | above 0 |
| Order needs a person | `sum(count_over_time({service_name="grid-api"} \|~ "needs review\|RefundNeedsFinance" [5m]))` | above 0 |
| Worker failing | `sum(count_over_time({service_name="grid-api"} \|= "worker iteration failed" [5m]))` | above 0 |
| Worker stopped | `sum(count_over_time({service_name="grid-api"} \|= "Bill payment worker heartbeat" [30m]))` | below 1, with **No data** set to Alerting |

For uptime, add `https://grid-api.eigenroot.com/health` under
**Testing & synthetics > Synthetics**.
