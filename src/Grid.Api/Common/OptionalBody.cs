using System.Text.Json;

namespace Grid.Api.Common;

/// <summary>
/// For commands whose body is optional (a reason, a note). A bare <c>POST</c> with no body and no
/// content type is as valid as one carrying JSON, which minimal API body binding doesn't allow.
/// </summary>
public static class OptionalBody
{
    public static async Task<T?> ReadAsync<T>(HttpRequest request, CancellationToken ct) where T : class
    {
        if (request.ContentLength is 0 or null && !request.HasJsonContentType()) return null;
        if (!request.HasJsonContentType()) throw new BadHttpRequestException("Expected a JSON body.");
        try
        {
            return await request.ReadFromJsonAsync<T>(ct);
        }
        catch (JsonException ex)
        {
            throw new BadHttpRequestException("The JSON body could not be read.", ex);
        }
    }
}
