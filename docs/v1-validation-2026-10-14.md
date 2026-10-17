# V1 validation and review handoff — 14/10/2026

Branch: `codex/refactor-v1`. Base: `b6d188b`. The original dirty checkout is preserved. This branch includes a snapshot of pre-existing README/test cleanup and contribution/review/setup/reference docs; the new implementation and aligned docs are described in [architecture](architecture.md) and [release notes](codex-v1-release-notes.md).

## Executed checks

| Check | Observed result |
| --- | --- |
| Pinned SDK | 10.0.303 on Windows |
| Release build | 0 warnings, 0 errors |
| Final suite after reviewer fix | 61 passed, 2 Unix-only skipped, 0 failed (63 total) |
| Focused unused-code analyzers | Exit 0: IDE0005, IDE0051, IDE0052, IDE0060 |
| Whitespace and Git diff checks | Pass |
| Four skill frontmatter validators | Pass |
| Independent skill scenarios | Host effort boundary, unavailable-tool fallback/ownership, incomplete phase/conflicting reviews, portable fake triage/verification |
| CI YAML and three PowerShell scripts | Parsed successfully |
| Distribution integration | ZIP/checksum/extraction; Unicode/spaces; fresh Git root; CLI doctor/classify/inspect/verify; real MCP stdio; WhatIf/collisions/tamper rejection |
| Smoke fixture assertions | total 4, classified 4, failedOrUnjudged 0, excludedWithReason 0, flagged 3 |

Checks are offline and remove TYPESAFE_API_KEY from subprocess environments. No live TypeSafe calls, model-quality benchmark, evaluator gold or company datasets were used. The Windows/Linux workflow was updated; Linux execution and Codex UI discovery were not exercised here.

## Independent code review

A fresh reviewer inspected the staged implementation against the plan, source and supplied checks. No Critical or Minor findings were reported. One Important finding blocked ZIP distribution: recursive skill-directory copying could include unrelated local notes, while the installer would reject that same ZIP.

Fix: publisher enumerates exactly eight intended SKILL.md/openai.yaml leaf files and copies no skill directory recursively. The isolated-source sentinel regression was observed failing with local-notes.txt in the ZIP, then passing after the fix; installation still succeeds. The whole suite and analyzers passed after this single fix pass. The reviewer did not rerun the suite independently; execution results were observed by the implementer. There was no second review dispatch.

## Decisions and remaining limits

- An ignored worktree preserves the original checkout; integrating the separate branch remains a user decision.
- Core extraction was added after the refactor request. CLI options/artifacts remain compatible; direct source consumers must adopt ReviewRequest.
- User-authored docs/test cleanup were preserved as a snapshot; distinguish them from newly authored changes.
- Live compatibility, quality, cost and latency are unverified; fake mechanics cannot support those claims.
- Codex UI/config acceptance is unverified; perform discovery checks on the company's client.
- Linux execution is pending CI; two platform-specific tests were skipped on Windows.
- Manifest-only terminal history is unbounded; use session-based MCP pilots or CLI processes for many runs.
- Hard-kill recovery/atomic artifacts are deferred; partial output cannot establish completion or safety.
- Link races, hostile Git config and malicious processes require OS isolation; path guards alone do not provide it.
- Hashes establish integrity relative to a trusted checksum, not publisher authenticity.
- Primary references were fetched by the implementer; no source/notices were copied. License/public publication needs the repo owner; skill scenarios do not benchmark model behavior.
- Independent review accepted supplied test evidence; the implementer ran the final suite after fixing its finding.

## Delivery

The local ZIP is created with `New-CodexRelease.ps1 -SkipChecks` after the complete equivalent Release checks above. Its release.json records source commit, dirty-state flag and bundled file hashes; the sibling .sha256 identifies the ZIP. No automatic merge, push, remote release, NuGet publication or license selection is performed. See [company quickstart](codex-v1-quickstart.md).
Final archive source commit: 2c6d8a4892f0a1faa6aeb50d54982c5cc865aa27; sourceDirty false; 24 bundled files. ZIP SHA-256: 2dba147b4438a25e9449a89860dc433d8e32eeb37dca584d9f4ef06b452c0c4a. Smoke on that published runtime: counts 4/4/0/0/3. This source commit precedes the documentation-only completion record.
