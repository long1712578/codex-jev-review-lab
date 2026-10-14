namespace JevReview;

public static class Evaluator
{
    public static IReadOnlyList<string> Labels => ReviewLabels.All;

    public static EvaluationMetrics Compute(GoldLabel[] gold, Verdict[] verdicts)
    {
        if (gold.Any(x => x == null) || verdicts.Any(x => x == null)) throw new ArgumentException("null_evaluation_record");
        if (gold.Any(x => string.IsNullOrWhiteSpace(x.Id) || !Labels.Contains(x.ExpectedLabel)) ||
            gold.Select(x => x.Id).Distinct().Count() != gold.Length ||
            verdicts.Select(x => x.Id).Distinct().Count() != verdicts.Length ||
            verdicts.Any(x => !gold.Any(g => g.Id == x.Id) || x.Status is not ("classified" or "unjudged" or "excluded") ||
                (x.Status == "classified" && (x.Prediction == null || !Labels.Contains(x.Prediction.Label)))))
            throw new ArgumentException("invalid_evaluation_ids_or_labels");
        var byId = verdicts.ToDictionary(x => x.Id);
        var matrix = Labels.ToDictionary(x => x, _ => Labels.Concat(["unjudged", "excluded", "missing"])
            .ToDictionary(x => x, _ => 0));
        int tp = 0, fp = 0, fn = 0, tn = 0, classified = 0, unjudged = 0, excluded = 0, missing = 0;
        List<string> falseNegativeIds = [];
        foreach (var expected in gold)
        {
            byId.TryGetValue(expected.Id, out var verdict);
            var prediction = verdict?.Status == "classified" ? verdict.Prediction!.Label : verdict?.Status ?? "missing";
            matrix[expected.ExpectedLabel][prediction]++;
            switch (verdict?.Status)
            {
                case "classified": classified++; break;
                case "unjudged": unjudged++; break;
                case "excluded": excluded++; break;
                default: missing++; break;
            }
            var flagged = verdict?.Flagged == true;
            if (expected.RequiresReview && flagged) tp++;
            else if (!expected.RequiresReview && flagged) fp++;
            else if (expected.RequiresReview) { fn++; falseNegativeIds.Add(expected.Id); }
            else tn++;
        }
        var latency = verdicts.Where(x => x.Attempts > 0 && !x.Reused).Select(x => x.LatencyMilliseconds).Order().ToArray();
        return new(gold.Length, classified, unjudged, excluded, missing, tp, fp, fn, tn,
            Ratio(tp, tp + fn), Ratio(tp, tp + fp), Ratio(tp + fp, gold.Length),
            Ratio(unjudged + excluded + missing, gold.Length), Ratio(classified, gold.Length), matrix,
            falseNegativeIds.ToArray(), Percentile(latency, .5), Percentile(latency, .95),
            Tokens(verdicts, x => x.InputTokens), Tokens(verdicts, x => x.OutputTokens));
    }

    private static double? Ratio(int numerator, int denominator) => denominator == 0 ? null : (double)numerator / denominator;
    private static double? Percentile(double[] values, double percentile) => values.Length == 0 ? null : values[(int)Math.Ceiling(values.Length * percentile) - 1];
    private static long? Tokens(Verdict[] verdicts, Func<JudgeResult, long?> selector) =>
        verdicts.Length == 0 || verdicts.Any(x => x.Reused || x.Prediction == null || selector(x.Prediction) == null)
            ? null : verdicts.Sum(x => selector(x.Prediction!)!.Value);
}

public static class EvaluationReporter
{
    public static async Task WriteAsync(string runDirectory, string goldPath, string? baselinePath, string outputDirectory, string inferenceRoot)
    {
        var fullGold = Path.GetFullPath(goldPath);
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(inferenceRoot));
        if (fullGold.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("gold_must_be_outside_inference_workspace");
        var gold = JsonFiles.Read<GoldLabel[]>(fullGold);
        var manifest = JsonFiles.Read<RunManifest>(Path.Combine(runDirectory, "manifest.json"));
        var predictions = JsonFiles.ReadLines<Verdict>(Path.Combine(runDirectory, "verdicts.jsonl"));
        var pipeline = Evaluator.Compute(gold, predictions);
        var baselineVerdicts = baselinePath == null ? null : JsonFiles.ReadLines<Verdict>(baselinePath);
        var baseline = baselineVerdicts == null ? null : Evaluator.Compute(gold, baselineVerdicts);
        if (baselineVerdicts != null && baselineVerdicts.Any(x => predictions.Any(p => p.Id == x.Id && p.InputHash != x.InputHash)))
            throw new ArgumentException("baseline_input_hash_mismatch");
        Directory.CreateDirectory(outputDirectory);
        await JsonFiles.WriteAsync(Path.Combine(outputDirectory, "metrics.json"), new
        {
            runId = manifest.RunId,
            mode = manifest.Configuration.Mode,
            manifest.InputHash,
            manifest.RubricHash,
            goldHash = JsonFiles.Hash(File.ReadAllText(fullGold)),
            pipeline,
            baseline,
            cost = (double?)null,
            codexVerificationMilliseconds = (double?)null,
            caveat = "Fake runs test plumbing only. Escalation metrics do not measure final finding correctness. Latency includes item retries/backoff; costs and Codex usage are N/A. Gold path placement alone is not a blind evaluation boundary; use the documented isolated container."
        });
        string Number(double? value) => value?.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) ?? "N/A";
        var report = $"# Pilot evaluation\n\nRun: {RunReporter.Escape(manifest.RunId)}. Mode: {manifest.Configuration.Mode}.\n\n" +
            "| Metric | A: direct review | B: Jev triage |\n| --- | --- | --- |\n" +
            $"| Recall | {Number(baseline?.Recall)} | {Number(pipeline.Recall)} |\n" +
            $"| Precision | {Number(baseline?.Precision)} | {Number(pipeline.Precision)} |\n" +
            $"| Escalation rate | {Number(baseline?.EscalationRate)} | {Number(pipeline.EscalationRate)} |\n" +
            $"| Coverage | {Number(baseline?.Coverage)} | {Number(pipeline.Coverage)} |\n" +
            $"| Error rate (includes excluded/missing) | {Number(baseline?.ErrorRate)} | {Number(pipeline.ErrorRate)} |\n\n" +
            $"Total gold items: {pipeline.Total}. Classified: {pipeline.Classified}; unjudged: {pipeline.Unjudged}; excluded: {pipeline.Excluded}; missing: {pipeline.Missing}.\n\n" +
            $"B false negatives: {string.Join(", ", pipeline.FalseNegativeIds.Select(RunReporter.Escape))}.\n\n" +
            "The full confusion matrix and denominators are in metrics.json. No claim about production quality, savings, or final findings follows from this pilot. Fake results measure pipeline mechanics only. No baseline supplied means A is N/A.\n";
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "evaluation.md"), report);
    }
}
