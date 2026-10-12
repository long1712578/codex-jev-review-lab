using System.Text.Json;

namespace JevReview;

public static class JevResponseParser
{
    public static JudgeResult Parse(string json, string questionId, ReviewRubric rubric)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var answers = root.GetProperty("answers");
            if (answers.ValueKind != JsonValueKind.Object || answers.EnumerateObject().Count() != 1 ||
                !answers.TryGetProperty(questionId, out var answer) || answer.GetProperty("type").GetString() != "choice")
                throw new JudgeException("invalid_response");
            var label = answer.GetProperty("choice").GetString()!;
            var probabilities = answer.GetProperty("probabilities").EnumerateObject()
                .ToDictionary(x => x.Name, x => x.Value.GetDouble(), StringComparer.Ordinal);
            var result = new JudgeResult(label, probabilities, answer.GetProperty("confidence").GetDouble(),
                root.GetProperty("model").GetString()!, Tokens(root, "input_tokens"), Tokens(root, "output_tokens"));
            Validate(result, rubric);
            return result;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or FormatException or OverflowException)
        {
            throw new JudgeException("invalid_response");
        }
    }

    public static void Validate(JudgeResult result, ReviewRubric rubric)
    {
        if (result == null || result.Probabilities == null || string.IsNullOrWhiteSpace(result.Model) || string.IsNullOrWhiteSpace(result.Label) ||
            !rubric.Labels.ContainsKey(result.Label) || result.Probabilities.Count != rubric.Labels.Count ||
            rubric.Labels.Keys.Any(x => !result.Probabilities.ContainsKey(x)) ||
            result.Probabilities.Values.Any(x => !double.IsFinite(x) || x < 0 || x > 1) ||
            Math.Abs(result.Probabilities.Values.Sum() - 1) > .001 ||
            result.Probabilities[result.Label] + .000001 < result.Probabilities.Values.Max() ||
            !double.IsFinite(result.Confidence) || result.Confidence < 0 || result.Confidence > 1 ||
            result.InputTokens < 0 || result.OutputTokens < 0)
            throw new JudgeException("invalid_response");
    }

    private static long? Tokens(JsonElement root, string name) =>
        root.TryGetProperty("usage", out var usage) && usage.TryGetProperty(name, out var value) ? value.GetInt64() : null;
}
