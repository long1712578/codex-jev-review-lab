---
name: ultrathink
description: Use when the user explicitly requests ultrathink for a difficult decision, investigation, or change with uncertain failure modes. Exclude routine edits and requests that only mention the word in code or quoted material.
---

# Ultrathink

Use the current model and available tools to investigate the requested task carefully. This skill does not change model settings or reasoning effort; those are controlled by the host. Give concise conclusions, evidence and decisions rather than private reasoning traces.

1. State the intended outcome, scope and material unknowns. Reuse the user's existing constraints and authorization; ask only for information that blocks a decision.
2. Read relevant instructions. Use CodeGraph first if the target repository has an index; otherwise locate implementations, callers and tests directly.
3. Compare the plausible approaches against concrete failure modes, boundary values and operational constraints. Choose the smallest change that satisfies the request.
4. Execute authorized work. Verify hypotheses against source, reproducible probes and appropriate tests; revise the approach when evidence contradicts it.
5. Return the outcome, supporting evidence, checks actually run and unresolved limitations. A release deadline does not turn an unverified assumption into a fact.

Example: `$ultrathink investigate why this API rejects empty orders, then implement and verify the smallest fix.`
