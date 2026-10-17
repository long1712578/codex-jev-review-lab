namespace JevReview;

public sealed record GoldLabel(string Id, string ExpectedLabel, bool RequiresReview, string Reason);
public sealed record EvaluationMetrics(int Total, int Classified, int Unjudged, int Excluded, int Missing,
    int TruePositive, int FalsePositive, int FalseNegative, int TrueNegative,
    double? Recall, double? Precision, double? EscalationRate, double? ErrorRate, double? Coverage,
    Dictionary<string, Dictionary<string, int>> ConfusionMatrix, string[] FalseNegativeIds,
    double? P50Milliseconds, double? P95Milliseconds, long? InputTokens, long? OutputTokens);
