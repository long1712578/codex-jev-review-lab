using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JevReview;

public static class JsonFiles
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public static readonly JsonSerializerOptions Pretty = new(Options) { WriteIndented = true };
    public static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
        ?? throw new ArgumentException("invalid_json_file");
    public static async Task WriteAsync<T>(string path, T value) =>
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, Pretty), new UTF8Encoding(false));
    public static T[] ReadLines<T>(string path) => File.ReadLines(path).Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => JsonSerializer.Deserialize<T>(x, Options) ?? throw new ArgumentException("invalid_json_line")).ToArray();
    public static async Task WriteLinesAsync<T>(string path, IEnumerable<T> values) =>
        await File.WriteAllLinesAsync(path, values.Select(Serialize), new UTF8Encoding(false));
}
