using System.Text;

namespace JevReview;

public static class RunReporter
{
    public static async Task WriteAsync(ReviewRun run)
    {
        Directory.CreateDirectory(run.OutputDirectory);
        await JsonFiles.WriteLinesAsync(Path.Combine(run.OutputDirectory, "verdicts.jsonl"), run.Verdicts);
        await JsonFiles.WriteAsync(Path.Combine(run.OutputDirectory, "flagged.json"),
            run.Verdicts.Where(x => x.Flagged).Select(x => new { x.Id, x.Path, x.Status, x.Reasons }).ToArray());
        await JsonFiles.WriteLinesAsync(Path.Combine(run.OutputDirectory, "errors.jsonl"), run.Verdicts.Where(x => x.Status == "unjudged"));
        await File.WriteAllTextAsync(Path.Combine(run.OutputDirectory, "report.md"), Report(run), new UTF8Encoding(false));
        // Manifest is the completion marker for persisted jobs, so write it after all result artifacts.
        await JsonFiles.WriteAsync(Path.Combine(run.OutputDirectory, "manifest.json"), run.Manifest);
    }

    public static string Report(ReviewRun run)
    {
        var c = run.Manifest.Counts;
        StringBuilder report = new($"# Review {Escape(run.Manifest.RunId)}\n\nMode: {run.Manifest.Configuration.Mode}. Models: {string.Join(", ", run.Manifest.ActualModels)}.\n\n");
        report.AppendLine($"Total: {c.Total} = classified {c.Classified} + failed/unjudged {c.FailedOrUnjudged} + excluded {c.ExcludedWithReason}. Flagged: {c.Flagged} (overlapping attribute).\n");
        report.AppendLine("## Model predictions\n\nThese are triage predictions, not verified findings or a safety approval.\n");
        report.AppendLine("| ID | Path | Status | Prediction | Needs review | Reason |\n| --- | --- | --- | --- | --- | --- |");
        foreach (var v in run.Verdicts)
            report.AppendLine($"| {Escape(v.Id)} | {Escape(v.Path)} | {v.Status} | {v.Prediction?.Label ?? "N/A"} | {v.Flagged} | {string.Join(", ", v.Reasons.Select(Escape))} |");
        report.AppendLine($"\n## Verification and coverage\n\nNo verified findings recorded by classification. {c.Flagged} items require review; {c.FailedOrUnjudged + c.ExcludedWithReason} have no model prediction. Non-flagged items have not been verified. Use the verify command with evidence to record confirmed, rejected or unresolved findings.\n");
        report.AppendLine($"Wall clock (before report I/O): {run.Manifest.WallClockMilliseconds:F1} ms. Cost: N/A. Codex verification time/tokens: N/A. Raw input is retained only in the original dataset/repository.\n\nRubric hash: `{run.Manifest.RubricHash}`. Input hash: `{run.Manifest.InputHash}`.");
        return report.ToString();
    }

    internal static string Escape(string text) => text.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ").Replace("<", "&lt;").Replace(">", "&gt;");
}
