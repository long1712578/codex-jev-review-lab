namespace JevReview;

public sealed record ReviewItem(string Id, string Task, string Path, string Diff, string Context);
public sealed record ReviewRubric(string Version, string Instructions, Dictionary<string, string> Labels);

public static class ReviewLabels
{
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly<string>(
        ["refactor_only", "behavior_change", "mixed", "insufficient_context"]);
}
