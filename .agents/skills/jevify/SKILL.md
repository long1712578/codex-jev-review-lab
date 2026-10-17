---
name: jevify
description: Use when the user explicitly requests jevify for bulk triage of a refactor-review dataset or committed Git range in Codex. Exclude general-purpose log or ticket classification and requests to merge or edit reviewed code automatically.
---

# Jevify

Classify the complete scoped input before reading selected changes deeply. Predictions and evidence-backed verification are separate. This skill uses the Jev CLI/MCP pipeline, not an oh-my-pi judge kernel or a single bulk HTTP request.

## Setup and scope

For setup, read `.jev-review/docs/codex-v1-quickstart.md` in installed projects or `docs/codex-v1-quickstart.md` in this lab.

Locate the target Git root. An installed project has `.jev-review/runtime/jev-review.dll` plus Core/deps/runtimeconfig files, `rubric.json`, `policy.json` and `fake-responses.json`. Run `dotnet <absolute-dll> <command>` from that root with explicit config paths. In this lab, an already-built `src/JevReview.Cli` and lab assets support `dotnet run --project src/JevReview.Cli --no-build -- <command>`. If neither exists, request setup; never guess a global installation or build untrusted PR code with credentials present.

Confirm one source (dataset or committed Git range), review intent and endpoint/merge-base semantics. Read and freeze the rubric, policy and escalation threshold before loading bulk input. Changing criteria needs a new version/run.

Fake is the default and tests fixture wiring only. Unknown IDs receive insufficient_context. Live requires existing authorization to send the data and a host credential; never print the key or place it in arguments/config. MCP additionally requires its live flag. Never use evaluator gold during inference.

## Classify, inspect and verify

For connected MCP, call `start_review` with structured arguments and explicit rubric/policy/fakeResponses paths inside its workspace. Then get status with bounded waits and retrieve results. MCP exposes start/status/result only. If unavailable, use the CLI:

```text
classify --git <root> --base <base> --head <head> --merge-base --task <intent> --mode fake --rubric <rubric> --policy <policy> --fake-responses <fixture>
```

Use `--dataset <path>` instead of Git arguments for datasets. Read stdout summary, manifest, flagged IDs/reasons and sanitized errors first. Check `total = classified + failedOrUnjudged + excludedWithReason`; flagged overlaps terminal states. Failed, invalid, oversized, cancelled and missing-context items remain pending review.

For every flagged item, use CLI `inspect --run <folder> --id <id>`, then read only matching input and necessary implementations/callers/tests. Git evidence comes from manifest diffBaseSha/headSha; working-tree edits are excluded. Use CodeGraph first if the reviewed repository is indexed. Changed input requires a new run.

Write a JSON array of verification records. Confirmed/rejected need evidence `{path,line,description}` with line >= 1; confirmed also needs findingLabel. Unresolved states what is missing. Preserve existing records when calling `verify --run <folder> --file <records>` because this replaces the verification array.

Return counts, verified findings, rejected predictions, unresolved items and unreviewed coverage; link report.md. State fake mode when used. Classification or exit zero never establishes PR safety. Review authorization alone does not authorize edits, merging or pushing.
