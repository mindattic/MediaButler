---
codex: 1
project: MediaButler
code: MB
layer: rfc
status: planned
updated: 2026-06-07
---

# RFC 0001 — Test strategy for the LLM fallback parser

## Problem
`LegionFallbackParser.ClassifyAsync` ([MediaButler/Llm/LegionFallbackParser.cs](../../MediaButler/Llm/LegionFallbackParser.cs))
classifies the long tail of folder names the regex `NameParser` can't handle. It is currently
unproven by automated tests: story [MB-US-F1](../USER_STORIES.md) is 🟡 because no test drives a
live or mocked `MindAttic.Legion` call. We cannot mark the LLM path ✅ under
[#MB-LAW-6](../BIBLE.md#MB-LAW-6) and [HOUSE-LAW-8](../../../MindAttic.HouseRules.md#HOUSE-LAW-8)
without verification.

## Options compared
1. **Live provider call in tests** — real network, real credentials. Flaky, costs money, leaks the
   Vault chain into CI. Rejected.
2. **Mock the `LegionClient` transport** — inject a fake that returns canned provider JSON, then
   assert `ClassifyAsync` maps it into the right `LlmGuess` (and returns `null` on garbage). Fast,
   deterministic, no network. Preferred.
3. **Test only `ExtractJsonObject` in isolation** — covers brace-balancing but not the
   enable-gate, kind mapping, or null-on-failure contract. Insufficient alone; fold into option 2.

## Decision
Option 2: make the transport seam injectable (or wrap it) so a fake provider response can be fed
in, and cover the public contract — disabled returns null, fenced/echoed JSON is extracted, an
"unknown" or titleless answer returns null, a valid answer maps to `LlmGuess`. Include the
option-3 brace-balancing cases.

## What NOT to do
- Do NOT call a real provider or read real credentials in tests ([#MB-LAW-7](../BIBLE.md#MB-LAW-7)).
- Do NOT hard-code a provider SDK to make it testable ([HOUSE-LAW-4](../../../MindAttic.HouseRules.md#HOUSE-LAW-4)).
- Do NOT let a test failure in the LLM path crash the pipeline — the production behaviour is
  non-fatal-skip and the test must preserve that contract.

## Phased plan (with risk)
1. Add a transport seam to `LegionFallbackParser` (low risk; touches app code — out of scope for
   the docs pass, tracked here as a follow-up).
2. Add `LegionFallbackParserTests` covering the contract above (low risk).
3. Promote [MB-US-F1](../USER_STORIES.md) to ✅ and cite the new test in [#MB-LAW-6](../BIBLE.md#MB-LAW-6).

## Graduates into
[BIBLE §5 MB-LAW-6](../BIBLE.md#MB-LAW-6), story [MB-US-F1](../USER_STORIES.md).
