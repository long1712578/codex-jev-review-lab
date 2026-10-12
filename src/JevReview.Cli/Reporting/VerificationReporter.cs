namespace JevReview;
public static class VerificationReporter
{
    public static void Validate(Verification[] verification, Verdict[] verdicts)
    {
        if (verification.Any(x => x == null) || verdicts.Any(x => x == null)) throw new ArgumentException("null_verification_record");
        if (verification.Select(x => x.Id).Distinct().Count() != verification.Length || verification.Any(x =>
            !verdicts.Any(v => v.Id == x.Id) || x.Status is not ("confirmed" or "rejected" or "unresolved") ||
            string.IsNullOrWhiteSpace(x.Summary) || x.Evidence == null ||
            (x.Status != "unresolved" && x.Evidence.Length == 0) ||
            x.Evidence.Any(e => e == null || string.IsNullOrWhiteSpace(e.Path) || e.Line < 1 || string.IsNullOrWhiteSpace(e.Description)) ||
            (x.Status == "confirmed" && (x.FindingLabel == null || !Evaluator.Labels.Contains(x.FindingLabel)))))
            throw new ArgumentException("verification_requires_known_ids_status_and_evidence");
    }

    public static async Task WriteAsync(string runDirectory, Verification[] verification)
    {
        var manifest = JsonFiles.Read<RunManifest>(Path.Combine(runDirectory, "manifest.json"));
        var verdicts = JsonFiles.ReadLines<Verdict>(Path.Combine(runDirectory, "verdicts.jsonl"));
        Validate(verification, verdicts);
        await JsonFiles.WriteAsync(Path.Combine(runDirectory, "verification.json"), verification);
        var report = RunReporter.Report(new(manifest, verdicts, runDirectory));
        var heading = report.IndexOf("## Verification and coverage", StringComparison.Ordinal);
        report = report[..heading] + "## Verified review records\n\n";
        foreach (var finding in verification)
        {
            report += $"- **{RunReporter.Escape(finding.Id)} — {finding.Status}** ({finding.FindingLabel ?? "N/A"}): {RunReporter.Escape(finding.Summary)}\n";
            foreach (var evidence in finding.Evidence)
                report += $"  - {RunReporter.Escape(evidence.Path)}:{evidence.Line}: {RunReporter.Escape(evidence.Description)}\n";
        }
        var unresolved = verification.Count(x => x.Status == "unresolved");
        var pending = verdicts.Where(x => x.Flagged && !verification.Any(v => v.Id == x.Id)).Select(x => x.Id).ToArray();
        report += $"\nUnresolved: {unresolved}. Flagged without review record: {pending.Length} ({string.Join(", ", pending.Select(RunReporter.Escape))}). Non-flagged without review record: {verdicts.Count(x => !x.Flagged && !verification.Any(v => v.Id == x.Id))}.\n\n";
        report += "Evidence is supplied by the reviewer; this CLI validates its structure and does not independently prove the finding. Model predictions remain separate above. Build/test checks remain required.\n";
        await File.WriteAllTextAsync(Path.Combine(runDirectory, "report.md"), report);
    }
}
