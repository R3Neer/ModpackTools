# Hybrid-core migration records

Current status, 2026-10-16: native Windows x64 package implemented and locally
accepted, including installer/update/Nu without PowerShell. The user subsequently
authorized personal activation; installation on D and the real Nu import are now
active and verified. Legacy retirement remains separate. Read the current
[activation handoff](native-activation-handoff.md) and
[public command measurements](performance.md) first. Numbered records preserve
historical scope; their old pending notes are not the current progress report.

All migration work belongs to `codex/hybrid-core`, forked from
`025b7f32200484070fe6ec5e5aa1a3ef49c3bfa2` (declared version 4.0.2).
The separate checkout avoids changing the user's `main` checkout or installation.

## Reading order

1. [Commit policy](commit-policy.md): checkpoints and validation gates.
2. [Compatibility contract](compatibility.md): preserved behavior and deliberate changes.
3. [Comparison matrix](comparison-matrix.md): acceptance cases and equality rules.
4. [Performance evaluation](performance.md): measurements, limits, and targets.
5. [Decisions](decisions.md): selected architecture, language, dependencies, and scope.
6. [Implementation sequence](implementation-plan.md): small independently reviewable capabilities.
7. [Architecture and review](architecture.md): logical boundaries and typed contracts.
8. [Error improvements](error-improvements.md): explicitly authorized differences.

## Documentation boundary

Keep here: architectural alternatives, rationale, migration contracts, differential
cases, performance baselines, raw evaluation data, transition plan, rollback gates,
and progress. This branch's `AGENTS.md` governs migration work; it is retained
with these records in the eventual complete merge.

Do not add migration announcements to the product README, public help, release
notes, or the existing product docs during evaluation. When a capability is
implemented, update shipped documentation only to describe actual supported
behavior, requirements, or installation; avoid plans and migration history there.

The user's clarification is to keep these documents out of `main` only until
integration. The eventual authorized integration is a complete branch merge,
including these documents, evaluation tooling and checkpoint history. This
supersedes the earlier selective product-only replay plan. No merge or push is
authorized by readiness preparation. See the [integration readiness checklist](integration-readiness.md).
