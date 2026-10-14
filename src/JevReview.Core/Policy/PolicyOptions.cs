namespace JevReview;

public sealed record PolicyOptions
{
    public double MinProbability { get; init; } = .8;
    public double SampleRate { get; init; } = .1;
    public string[] AlwaysReviewPaths { get; init; } = ["authorization", "authentication", "transaction"];
}
