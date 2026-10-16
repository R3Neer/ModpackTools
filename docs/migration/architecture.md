# Architecture and review method

Implementation authorized on 2026-10-12. Three roles: implementer, independent
black-box reviewer, and architecture/white-box reviewer. Blindness is a working
restriction, not filesystem isolation. Reviewers do not implement the product.

## Logical capabilities extracted from the baseline

| Capability | Inputs / outputs | Effects and boundary |
|---|---|---|
| Version | No project required -> VersionInfo | No configuration, renderer or network reads. |
| Project context | Explicit/session ID + configuration -> ProjectContext | Read PSD1 and pack manifest; validate identities and duplicate registrations. |
| Inventory | Context + query -> InventorySnapshot / selected result | Read managed/local content and editorial metadata; references persisted separately. |
| Artifact declarations | Bounded JAR/ZIP -> dependency declarations / unknown | No executing mods; retain loader/side semantics. |
| Provider lookup | Typed identity/query -> candidates | Bounded HTTP, cancellation, timeout, request deduplication. |
| Resolver | Snapshot + candidates + request -> proposed solution | Pure selection; reproduce costs, pins, downgrade and search-limit semantics. |
| Planning | Solution + protected source snapshot -> bound plan | Read/hash; no project writes. Bind project identity and full source fingerprint. |
| Application/recovery | Bound plan + exclusive ownership -> durable outcome | Stage, journal, backups, preconditions, per-file replacement, rollback/recovery. |
| Presentation | Typed result/events -> shell display or public JSON | Adapter owns R3CLI and interactive input; domain never captures rendered objects. |
| Distribution | Verified package -> selected version | Separate helper, atomic manifest switch, rollback and deferred cleanup. |

This is a responsibility map, not a requirement for one class/interface per row.
Detailed typed contracts are reviewed before each slice; no empty abstractions are
added just to fill the diagram. Domain never depends on CLI, console or PS state.

## First typed boundary

`ProductVersion.Current : VersionInfo` is independent of transport. CLI maps it to
the public success envelope. Separate success/failure DTOs preserve absent fields
and explicit null error targets. Source-generated JSON metadata is required.

Internal request v1: object with required `schema_version` (integer 1) and
`arguments` (array of strings), and optional typed session `context` described in
[data-contracts.md](data-contracts.md). Unknown/duplicate fields, null/non-string arguments,
NUL, unsupported schema and invalid UTF-8/JSON fail with native exit 2. Maximum
request size is 1 MiB, nesting 16 and argument count 1024. UTF-8 BOM accepted.
Invocation: `modpacktools --request-path <file>`, with no additional host arguments.
The file separates request transport from stdin reserved for future question replies.

Currently one public schema-1 final envelope is written on stdout, without an
event wrapper. Success exit 0, domain failure exit 1, transport failure exit 2.
There are no progress/question events in the first capability. Any future event
framing requires a separately versioned protocol and coordinated adapter changes.
Legacy bridge keeps its existing exit policy; adapters parse native nonzero
structured failures before deciding how to present them.

## Review and commit loop

1. Extract observable legacy rules, effects, errors and intentional differences.
2. Propose typed input/output contracts and complete operation flow.
3. Architecture reviewer checks ownership and dependencies before implementation.
4. Implement a small complete capability with idiomatic C# and native acceptance.
5. Blind reviewer independently derives and executes differential cases from PS.
6. White-box reviewer reads full code, tests and measurements; checks AOT,
   resources, hidden work, dependencies and idioms. Address concrete findings.
7. Once applicable gates pass, commit immediately before the next capability.

Implementer owns performance measurements. Reviewers challenge comparability and
coverage; external timings cannot substitute for profiling, or vice versa.
