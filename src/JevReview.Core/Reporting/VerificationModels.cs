namespace JevReview;

public sealed record Evidence(string Path, int Line, string Description);
public sealed record Verification(string Id, string Status, string Summary, Evidence[] Evidence, string? FindingLabel = null);
