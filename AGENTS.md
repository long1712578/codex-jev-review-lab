<!-- CODEGRAPH_START -->
## CodeGraph

If `.codegraph/` exists at the repository root, use `codegraph_explore` or `codegraph explore` before text searches or file reads to understand or locate code. If absent, skip CodeGraph; do not index automatically.
<!-- CODEGRAPH_END -->

## Build and checks

- .NET SDK is pinned by `global.json` (10.0.303, latest patch allowed).
- Restore: `dotnet restore JevReview.slnx`
- Build: `dotnet build JevReview.slnx --no-restore`
- Offline tests: `dotnet test JevReview.slnx --no-restore`
- Smoke: `dotnet run --project src/JevReview.Cli --no-build -- classify --dataset datasets/smoke --mode fake`
- Default CI has no API credential and only runs offline tests.

## Review lab invariants

Freeze rubric before loading bulk inputs. First read the summary, flagged IDs and error codes, then fetch context for the selected items. `refactor_only` is a prediction; it does not establish that code is safe. Reports keep predictions and evidence-backed verification separate.

Keep `total = classified + failedOrUnjudged + excludedWithReason`. Failed, oversized, invalid and cancelled items must remain visible. `flagged` overlaps terminal states. Never silently truncate input or convert an API/schema error to a pass.

Never print credentials, full datasets, HTTP error bodies or request bodies to command output. Use `TYPESAFE_API_KEY` only in live mode. Live smoke/evaluation is a separate explicit action; do not run it in default tests. Do not run untrusted PR code with a key in the environment. No automatic edits, merge or push are part of this tool.

Holdout gold belongs outside the inference workspace. For a blind benchmark, use the restricted mounts in `docs/experiment-plan.md`; a path check or instruction alone does not isolate the reviewing agent. Fake fixtures are test responses, never ground truth or evidence of model quality.

CLI and MCP share collection, judge client, policy and reporting. MCP accepts structured arguments, rejects paths outside its configured workspace and linked path segments, and never accepts arbitrary shell commands. Git range review excludes staged, unstaged and untracked changes and records resolved SHAs and diff semantics.
