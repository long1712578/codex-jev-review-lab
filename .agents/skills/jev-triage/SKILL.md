---
name: jev-triage
description: Triage a refactor review dataset or committed Git diff with the Jev Review CLI, then verify flagged changes against source and tests. Use for reviews that ask whether a claimed refactor changes business behavior.
---

Use the local Jev Review lab to classify bulk inputs before reading them into the main context. This workflow reads and reports; it does not authorize editing or merging the reviewed repository.

1. Establish the review task, scope and rubric first. Default rubric: `rubrics/refactor-review.v1.json`; policy: `configs/policy.json`. A changed rubric needs a new version/run. Read the rubric before the bulk dataset.
2. From the lab root, run `dotnet run --project src/JevReview.Cli --no-build -- classify --dataset <path> --mode fake --policy configs/policy.json`. For committed changes use `--git <repository> --base <ref> --head <ref> --task "<intent>"`, adding `--merge-base` when reviewing changes since divergence from the target branch. Use live only when the user has authorized sending this data to Jev and provided credentials; fake tests mechanics only.
3. Initially read only stdout counts, `manifest.json`, `flagged.json` and `errors.jsonl`. Do not load all diffs/verdicts into context. Check `total = classified + failedOrUnjudged + excludedWithReason`; excluded items also appear in flagged with a reason. Treat unjudged, oversized and missing context as pending review, never a pass.
4. For selected IDs, `inspect --run <run-directory> --id <id>` returns that item's verdict. Read only the matching dataset item or Git file diff, then obtain the relevant implementations, callers and tests. Use CodeGraph first when the reviewed repo has `.codegraph/`. For Git runs use the manifest's `diffBaseSha`/`headSha`, not mutable refs or the current working tree. If the input changed since collection, start a new run.
5. Verify every flagged item, including quality samples and always-review paths. Prefer concrete source/caller/test evidence. Record `confirmed`, `rejected` or `unresolved`; for confirmed findings provide a finding label and evidence with path, positive line number and description. A model probability is not evidence. Where context is unavailable, explain what is missing.
6. Save records as a JSON array and run `verify --run <run-directory> --file <records.json>`. Example record:

   ```json
   {"id":"dev-006","status":"confirmed","summary":"Quantity 10 now receives the discount.","findingLabel":"behavior_change","evidence":[{"path":"src/Orders/Quantity.cs","line":1,"description":"Condition changes from > 10 to >= 10."}]}
   ```

7. Report counts, confirmed findings and evidence, rejected predictions, unresolved items and unreviewed coverage. Link `report.md`. Keep Jev predictions separate from verified findings and state when a fake backend was used. Build/tests are separate checks; do not declare a PR safe from classification alone.

Never read evaluator gold during inference. For evaluation handoff, finish predictions and verification artifacts before the evaluator receives gold. See `docs/experiment-plan.md` only when benchmarking is requested. See `README.md` for installation, timeout/retry/resume options and the optional MCP adapter.
