using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.Options;

namespace Grid.Api.Common;

/// <summary>
/// The wrapper every response is sent in. <c>data</c> is omitted entirely for void commands,
/// which is the contract GRID Pro's client is already written against.
/// </summary>
public sealed record ApiEnvelope<T>(
    bool Success,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] T? Data,
    ApiErrorBody? Error,
    string TraceId,
    DateTimeOffset Timestamp);

public sealed record ApiErrorBody(
    string Code,
    string Message,
    object? Detail,
    IReadOnlyList<FieldError> Fields);

public sealed record FieldError(string Field, string Message);

public static class Envelope
{
    public static EnvelopeResult<T> Ok<T>(T data) => new(data, StatusCodes.Status200OK);

    public static EnvelopeResult<T> Created<T>(T data) => new(data, StatusCodes.Status201Created);

    public static EnvelopeResult<object> Empty() => new(null, StatusCodes.Status200OK);

    internal static string TraceId(HttpContext context) =>
        Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

    internal static JsonSerializerOptions JsonOptions(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;

    public static Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message,
        object? detail = null,
        IReadOnlyList<FieldError>? fields = null)
    {
        var body = new ApiEnvelope<object>(
            false,
            null,
            new ApiErrorBody(code, message, detail, fields ?? []),
            TraceId(context),
            DateTimeOffset.UtcNow);
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(body, JsonOptions(context));
    }
}

/// <summary>A successful enveloped response; also tells OpenAPI what the endpoint returns.</summary>
public sealed class EnvelopeResult<T>(T? data, int statusCode)
    : IResult, IStatusCodeHttpResult, IEndpointMetadataProvider
{
    public int? StatusCode => statusCode;

    public Task ExecuteAsync(HttpContext httpContext)
    {
        var body = new ApiEnvelope<T>(
            true,
            data,
            null,
            Envelope.TraceId(httpContext),
            DateTimeOffset.UtcNow);
        httpContext.Response.StatusCode = statusCode;
        return httpContext.Response.WriteAsJsonAsync(body, Envelope.JsonOptions(httpContext));
    }

    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder) =>
        builder.Metadata.Add(new ProducesResponseTypeMetadata(
            StatusCodes.Status200OK, typeof(ApiEnvelope<T>), ["application/json"]));
}
