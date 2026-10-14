---
name: orchestrate
description: Use when the user explicitly requests orchestrate for substantial independent workstreams that benefit from delegation. Exclude a single small task and tightly dependent steps.
---

# Orchestrate

Coordinate the requested outcome and own the final integrated result. Inspect the actual tool catalog and applicable project policy before delegating. If subagent tools are unavailable or prohibited, execute the same workstreams sequentially and state that fallback.

1. Identify the deliverables, constraints, dependencies and acceptance checks. Preserve existing authorization and scope.
2. Delegate only substantial independent work. Each assignment includes its question, relevant files/context, permitted side effects, expected output and checks. Give each writable file one owner; dependent work waits for its inputs.
3. While workers run, complete independent coordinator work. Wait when no useful local work remains. Track outputs and unresolved blockers; a completed tool call is not proof a worker finished.
4. Reconcile findings against source, tests and the task contract. Resolve duplicate or contradictory results before integrating changes; inspect patches and run appropriate integrated checks.
5. Return the delivered outcome, verification evidence and remaining work. Keep incomplete or blocked work visible. The coordinator remains accountable for completion.

Example: `$orchestrate review independent API and persistence changes, then consolidate evidence and run the integration checks.`
