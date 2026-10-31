namespace Grid.Api.Common;

/// <summary>Which GRID app made the request, from the <c>X-Grid-App</c> header.</summary>
public enum AppKind { Unknown, Consumer, Pro }

public static class AppKindHeader
{
    public const string Name = "X-Grid-App";

    public static AppKind From(HttpContext context) =>
        context.Request.Headers[Name].ToString().Trim().ToLowerInvariant() switch
        {
            "consumer" or "home" => AppKind.Consumer,
            "pro" => AppKind.Pro,
            _ => AppKind.Unknown,
        };
}
