using Grafana.OpenTelemetry;
using OpenTelemetry;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Grid.Api.Common;

/// <summary>
/// Sends traces, metrics and logs to Grafana over OTLP. Off unless
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set, so local runs and CI export nothing.
/// Database queries are deliberately not traced: the payment worker polls every few seconds.
/// </summary>
public static class Telemetry
{
    private const string HealthPath = "/health";
    private const string CallbackPrefix = "/api/v1/hubtel/callbacks";

    public static WebApplicationBuilder AddGridTelemetry(this WebApplicationBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])) return builder;

        void Configure(GrafanaOpenTelemetrySettings settings)
        {
            settings.ServiceName = "grid-api";
            settings.DeploymentEnvironment = builder.Environment.EnvironmentName.ToLowerInvariant();
        }

        builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
        {
            options.Filter = context => !context.Request.Path.StartsWithSegments(HealthPath);
            // Hubtel callback URLs carry a signature in the path; it must not leave the server.
            options.EnrichWithHttpRequest = (activity, request) =>
            {
                if (request.Path.StartsWithSegments(CallbackPrefix)) activity.SetTag("url.path", CallbackPrefix);
            };
        });
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.UseGrafana(Configure))
            .WithMetrics(metrics => metrics.UseGrafana(Configure));
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.UseGrafana(Configure);
        });
        return builder;
    }
}
