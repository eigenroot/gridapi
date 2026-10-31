namespace Grid.Api.Common;

/// <summary>Turns every failure into the standard envelope so clients only parse one shape.</summary>
public sealed class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ApiException ex) when (!context.Response.HasStarted)
        {
            await Envelope.WriteErrorAsync(context, ex.StatusCode, ex.Code, ex.Message, ex.Detail, ex.Fields);
        }
        catch (BadHttpRequestException ex) when (!context.Response.HasStarted)
        {
            // Malformed JSON, wrong content type, unparseable route values.
            logger.LogInformation(ex, "Rejected malformed request");
            await Envelope.WriteErrorAsync(context, StatusCodes.Status400BadRequest,
                "request.malformed", "The request could not be read.");
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client went away; nothing to send.
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            logger.LogError(ex, "Unhandled error");
            await Envelope.WriteErrorAsync(context, StatusCodes.Status500InternalServerError,
                "server.error", "Something went wrong on our side.");
        }
    }
}
