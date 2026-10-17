namespace JevReview;

public abstract record ReviewSource;
public sealed record DatasetReviewSource(string Path) : ReviewSource;
public sealed record GitReviewSource(string Repository, string Base, string Head, string Task, bool MergeBase = false) : ReviewSource;
public sealed record ReviewRequest(ReviewSource Source, string RubricPath, RunOptions Options, string OutputDirectory,
    string? PolicyPath = null, string? FakeResponsesPath = null, string? ResumeDirectory = null);
