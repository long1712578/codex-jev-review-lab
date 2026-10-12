# Pilot A/B: Codex direct review versus Jev + Codex

## Freeze the experiment

Tune rubric, probability threshold and sampling on 20 dev inputs. Freeze them before the 20 holdout inputs, which use different change families. This is a small synthetic pilot; if holdout informs tuning, replace it.

An independent evaluator creates gold outside inference: id, expectedLabel, requiresReview, reason. Declare positives through requiresReview before inference; inspect behavior changes separately when needed. Fake fixtures are mock responses, never gold.

A reviews all inputs directly with Codex. B uses live Jev, frozen policy and Codex verification of flagged/quality samples. Keep the same input/source snapshot, intent, Codex model/settings and permitted context. Use fresh agent sessions. Record environment, actual models, wall clock, hashes and costs where measurable. Cold-cache and warm/resume experiments must be reported separately.

## Actual access boundary for gold

A file outside the repo is a placement rule, not an access boundary for an agent with host access. Run inference in a restricted container/sandbox with no gold mount, host root mount, Docker socket or evaluator tools. The evaluator receives inference outputs only after they are frozen.

Prepare a bundle containing only published app, unlabeled input and rubric:

```powershell
.\scripts\prepare-inference.ps1 -Dataset holdout -Output runs/inference-bundle
$bundle = (Resolve-Path -LiteralPath runs/inference-bundle).Path
docker run --rm --network none --read-only --cap-drop ALL --security-opt no-new-privileges --tmpfs /tmp `
  --mount "type=bind,source=$bundle/app,target=/app,readonly" `
  --mount "type=bind,source=$bundle/dataset,target=/dataset,readonly" `
  --mount "type=bind,source=$bundle/rubrics,target=/rubrics,readonly" `
  --mount "type=bind,source=$bundle/out,target=/out" `
  -w /app mcr.microsoft.com/dotnet/runtime:10.0 `
  dotnet /app/jev-review.dll classify --dataset /dataset --rubric /rubrics/refactor-review.v1.json --mode fake --output /out/run
```

This validates mechanics/isolation, not Jev quality. Docker execution was not tested during implementation. Pin the runtime image digest in the experiment log. For live classification allow network, add `-e TYPESAFE_API_KEY` using a host environment secret without printing its value, and keep the same mounts. The holdout bundle intentionally contains no fake fixture; fake will return insufficient_context for all items.

The separately running Codex reviewers for A/B also need actual access controls with only authorized input/source/context mounted. A CLI container alone does not isolate the Codex agent. Gold must remain inaccessible to both agents, including their shell/file tools. Do not build/test untrusted PR code in an environment holding an API key.

## Predictions, verification and metrics

A baseline emits verdicts.jsonl using schemas/verdict.schema.json, with the same IDs/input hashes and flagged=true for changes requiring review. Document how the four-way prediction and probability distribution were encoded; ad hoc reviewer certainty is not a calibrated probability. Save final findings separately in verification.json with path/line/evidence.

B produces verdicts automatically. Codex verifies flagged items, including failures/exclusions, and writes confirmed/rejected/unresolved records through verify. Missing implementation/caller stays unresolved; non-flagged items outside the frozen sample remain unverified.

After inference, evaluate with external gold and optional independent baseline:

```text
evaluate --run <B-run> --gold <external-gold.json> --baseline <A-verdicts.jsonl> --output <evaluation-directory> --inference-root <inference-workspace>
```

The evaluator rejects gold inside the declared workspace, invalid IDs and different baseline input hashes. This placement check prevents accidents; the container/sandbox provides the real blind boundary.

Use all gold items in denominators. Missing required-review predictions become false negatives. Failed/excluded items that are flagged count as escalations but have no valid classified label; assess coverage/error rate with recall. Zero denominators yield N/A. The confusion matrix includes four labels plus unjudged/excluded/missing. Inspect false negative IDs and severity.

Current latency metrics are per item, including retries/backoff. Codex verification time/tokens, agent context bytes, per-HTTP-attempt latency, full pipeline cost and final-finding correctness are not instrumented; measure externally or report N/A. Token sums are N/A when usage is incomplete. Without independent baseline, A is N/A. No genuine A/B benchmark is bundled because no credential, baseline or holdout gold was supplied.

Success: no lost item, traceable input/rubric/config, planted severe cases escalated and verified, concrete evidence, unresolved coverage visible, both advantages and regressions of B reported. A 40-case pilot does not establish production quality.
