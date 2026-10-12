# Implementation checklist

Specification: `codex-jev-review-lab-plan.md`. The user's request authorizes implementation in the current checkout. Keep the source plan unchanged.

Architecture: .NET 10 console app, shared collection/judging/policy/reporting services, offline xUnit tests. No application package dependencies. MCP is a small stdio adapter over the same review service. Live execution and real A/B results require user credentials and independent reviews; no fabricated measurements.

Global constraints: rubric before collection; preserve every collected item; never echo diffs, credentials or remote error bodies; distinguish prediction from verified finding; no arbitrary shell from MCP; gold unavailable in inference workspace; default CI offline.

1. [x] Core contracts, response validation and configurable policy. Tests: missing question ID, unknown labels, invalid distributions, selected probability vs confidence, sensitive paths, deterministic sampling.
2. [x] Dataset collector, fake/live judge, bounded runner, retry/timeout/cancellation, reports and resume. Tests: HTTP request contract and error cases, count conservation, oversize input, cache invalidation, failed item retention.
3. [x] CLI doctor/classify/inspect/verify/evaluate and datasets. Tests: non-echo output, command validation, metrics denominators and evidence, fake demo. Ship 20 dev and 20 disjoint holdout patterns; keep fake responses separate from input and gold.
4. [x] Git collector and stdio MCP. Tests: resolved SHA semantics, filenames with spaces, binary diffs, background job status, workspace path restrictions and protocol handling.
5. [x] Codex skill, Vietnamese README, experiment protocol, scripts and offline CI. Independent final review completed; regression tests cover substantive findings. See final verification notes below.

Shared interfaces: ReviewItem -> IJudgeClient -> JudgeResult -> ReviewRunner/ReviewPolicy -> Verdict/RunManifest -> reports, evaluator and MCP. Keep the four labels and three terminal states identical across every consumer.

Review focus: secret redaction; cancellation with queued items; stale cache across inputs/rubric/model; evaluator with missing or failed predictions; MCP traversal/symlink paths and Git options injection.

## Verification limits

Windows offline validation and Release builds are performed locally. Unix-specific symlink/case-sensitivity tests run in Linux CI and are skipped on this Windows machine. A live Jev smoke, genuine A/B benchmark, actual Codex UI/skill session and Docker execution require external prerequisites and were not run. Implemented entry points are ready; these experiment milestones are not represented as completed measurements. No external publication or commit was requested.

Final local evidence: Release suite **48 passed, 2 Unix-only skipped, 0 failed** (50 tests); Release build **0 warnings, 0 errors**. Smoke accounted for 4/4 items; dev accounted for 20/20; resume reused all 20 unchanged successful predictions; verification, external-gold toy evaluation and inference-bundle publication succeeded. Independent review findings were fixed and covered with regressions.
