namespace JevReview;

public static class ReviewService
{
    public static ReviewRubric LoadRubric(string path)
    {
        var rubric = JsonFiles.Read<ReviewRubric>(path);
        if (string.IsNullOrWhiteSpace(rubric.Version) || string.IsNullOrWhiteSpace(rubric.Instructions) || rubric.Labels == null ||
            !rubric.Labels.Keys.Order().SequenceEqual(ReviewLabels.All.Order()) || rubric.Labels.Values.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("invalid_rubric");
        return rubric;
    }

    public static HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    public static async Task<ReviewRun> StartAsync(ReviewRequest request, CancellationToken cancellationToken, string? apiKey = null)
    {
        // Freeze and validate rubric/configuration before collecting bulk input.
        var rubric = LoadRubric(request.RubricPath);
        var fakePath = request.FakeResponsesPath;
        var options = request.Options with
        {
            Policy = request.PolicyPath != null ? JsonFiles.Read<PolicyOptions>(request.PolicyPath) : request.Options.Policy,
            FakeFixtureHash = request.Options.Mode == "fake" && File.Exists(fakePath) ? JsonFiles.Hash(File.ReadAllText(fakePath!)) : null
        };
        ReviewRunner.ValidateOptions(options);
        ReviewItem[] items;
        Dictionary<string, string> source;
        if (request.Source is GitReviewSource git)
        {
            var collected = await GitCollector.CollectAsync(git.Repository, git.Base, git.Head, git.Task, git.MergeBase, cancellationToken);
            items = collected.Items;
            source = collected.Source;
        }
        else if (request.Source is DatasetReviewSource dataset)
        {
            var path = Path.GetFullPath(dataset.Path);
            items = DatasetCollector.Load(path);
            source = new() { ["kind"] = "dataset", ["path"] = path };
        }
        else throw new ArgumentException("choose_dataset_or_git");
        var output = request.OutputDirectory;
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new ArgumentException("output_directory_not_empty");
        using var http = CreateHttpClient();
        IJudgeClient client = options.Mode == "live" ? new JevJudgeClient(http,
            !string.IsNullOrWhiteSpace(apiKey) ? apiKey : throw new ArgumentException("missing_TYPESAFE_API_KEY"), options) :
            new FakeJudgeClient(File.Exists(fakePath) ? JsonFiles.Read<Dictionary<string, FakeAnswer>>(fakePath!) : new());
        return await new ReviewRunner(client).RunAsync(items, rubric, options, output, cancellationToken, request.ResumeDirectory, source);
    }
}
