---
name: workflowz
description: Use when the user explicitly requests workflowz for a broad migration, investigation, or adversarial review requiring several dependent phases and durable progress. Exclude a single small edit and oh-my-pi kernel execution requests.
---

# Workflowz

Run the requested work in verifiable phases using the actual Codex tools. This skill supplies a workflow, not an eval kernel or deterministic agent execution. Use available subagents for independent work only when permitted; otherwise run sequentially.

Create or reuse a task-specific ledger in the project's approved scratch location. Record the goal, input revision, constraints and phases. Each phase has an owner, inputs, expected output, acceptance checks, status and evidence links. Preserve existing work and authorization.

Execute dependencies in order. While a phase is incomplete, its dependents remain pending. Record findings and actual command results as work proceeds so another turn can resume without repeating completed work. Mark blocked work with the missing input, not success.

When reviews conflict, compare their claims with the agreed contract, source and reproducible checks. Record the decision and evidence; unsupported findings stay unresolved. Integrate outputs, then run checks across their boundaries.

Before reporting completion, reconcile every phase with its deliverable and verification evidence. Return the outcome, ledger/artifact paths and remaining coverage. A positive reviewer opinion cannot complete an unverified phase. Do not invent `eval`, `agent()`, `wait()` or `workpool()` APIs.

Example: `$workflowz migrate the authorization module using docs/plan.md and preserve evidence for every phase.`
