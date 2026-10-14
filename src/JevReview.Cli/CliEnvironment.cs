namespace JevReview;

internal static class CliEnvironment
{
    public static string RequireApiKey() => Environment.GetEnvironmentVariable("TYPESAFE_API_KEY") is { Length: > 0 } key
        && !string.IsNullOrWhiteSpace(key) ? key : throw new ArgumentException("missing_TYPESAFE_API_KEY");
}
