using System.Globalization;

namespace JevReview;

public sealed class CommandOptions
{
    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);

    public static CommandOptions Parse(string[] args)
    {
        var result = new CommandOptions();
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("expected_named_option");
            var key = args[i][2..];
            var value = key is "check-live" or "merge-base" or "allow-live" ? "true" :
                ++i < args.Length ? args[i] : throw new ArgumentException("missing_option_value");
            if (!result.values.TryAdd(key, value)) throw new ArgumentException("duplicate_option");
        }
        return result;
    }

    public void Allow(params string[] names)
    {
        if (values.Keys.Any(x => !names.Contains(x))) throw new ArgumentException("unknown_option");
    }

    public bool Has(string name) => values.ContainsKey(name);
    public string? Optional(string name) => values.GetValueOrDefault(name);
    public string Get(string name, string fallback) => Optional(name) ?? fallback;
    public string Require(string name) => Optional(name) ?? throw new ArgumentException("missing_required_option");
    public int Integer(string name, int fallback) =>
        int.TryParse(Get(name, fallback.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var value) ? value : throw new ArgumentException("invalid_integer_option");
}
