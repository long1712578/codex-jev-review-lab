namespace JevReview;

public sealed class ReviewPolicy(PolicyOptions options)
{
    public string[] Reasons(ReviewItem item, JudgeResult result)
    {
        List<string> reasons = [];
        if (result.Label != "refactor_only") reasons.Add(result.Label);
        if (result.Label == "refactor_only" && result.Probabilities[result.Label] < options.MinProbability)
            reasons.Add("low_probability");
        if (options.AlwaysReviewPaths.Any(x => item.Path.Contains(x, StringComparison.OrdinalIgnoreCase)))
            reasons.Add("always_review_path");
        if (reasons.Count == 0 && Sample(item.Id) < options.SampleRate) reasons.Add("quality_sample");
        return [.. reasons];
    }

    private static double Sample(string id)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id));
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(hash) / ((double)uint.MaxValue + 1);
    }
}
