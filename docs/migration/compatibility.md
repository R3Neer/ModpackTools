# Compatibility contract

Baseline: `025b7f32200484070fe6ec5e5aa1a3ef49c3bfa2`, declared 4.0.2.
Status: incremental implementation. Completed capabilities are recorded in
[implementation-plan.md](implementation-plan.md); remaining obligations are gates.
The user also authorized targeted [error improvements](error-improvements.md).

## Preserved public behavior

| Area | Obligation | Reason |
|---|---|---|
| Grammar | Preserve the 4.x domains, subcommands, documented aliases, option spelling, forbidden combinations, and Nu-specific validation. Do not restore retired commands. | Existing scripts and habits remain usable. |
| Context | Preserve explicit-project precedence, session selection, and isolation between projects. Nu `project use` updates `MODPACKTOOLS_PROJECT` in its parent session. | A child executable cannot mutate its parent's environment. |
| Identity | Preserve provider IDs, loader IDs, stable content IDs, ambiguity rejection, and separate numbered-reference scopes. | Display names and numbers are not durable identities. |
| Inventory | Preserve items, filters, categories, sides, pins, active/inactive state, priority, numbering, and public item ordering. | Later commands consume the saved selection. |
| Pipelines | Nu normal mode emits no machine value; `--no-human` returns the same unwrapped record/list. PS `--json` emits exactly one public schema-1 envelope. | No pipeline break while internals change. |
| Errors | Preserve domain error IDs, public categories, diagnostic meaning, relevant target data, and failure semantics. | Consumers need stable failures, not only success parity. |
| Validation | Preserve conflict/warning/unknown separation, strict policy, baseline-conflict policy, client/server scopes, and unsupported-expression uncertainty. | Missing evidence must not become compatibility. |
| Resolution | Preserve version selection, cost priorities, pins, local-artifact protection, explicit/transitive intent, downgrades, cycles, and the 10000-state failure limit. | A different valid solution may cause unwanted changes. |
| Mutations | Preserve affected files, unrelated bytes, resource ordering, batch all-or-nothing behavior, no-op behavior, and existing per-command confirmation rules. | The migration is not a grammar or interaction redesign. |
| Transactions | Preserve staging, project exclusion, linked-path policy, hashes, journal-before-write, rollback, external-edit protection, and recovery. | Correctness is a prerequisite for speed. |
| Packwiz | Preserve the external executable revision and hashes from `dependencies.psd1`, provider boundaries, and prepared-version checks. | Replacing Packwiz is outside scope. |
| Configuration | Preserve config override, project descriptors, metadata, categories, pins, intent, and theme. | A runtime change must not lose user state. |
| Presentation | Preserve useful content, help catalogue, role colors, ASCII, redirection, theme extensions, and functional terminal behavior. | Native adapters must still feel like the same CLI. |
| Builds | Preserve declared-dependency policy, semantic export validation, previous-artifact preservation, freshness reporting, and diff meaning. | A faster invalid distribution is unacceptable. |

## Public schema versus internal protocol

The legacy public envelope remains `schema_version = 1` with `ok`, `command`,
`arguments`, and either `data` or `error`. Preserve absent fields versus null,
array order, primitive types, nested payloads, and command-specific Nu unwrapping.
Do not silently add domain-model objects to a pipeline that currently returns none.

The new executable uses an independently versioned request/event protocol. Requests
carry protocol version, request ID, argument tokens, invocation context, and
interaction policy. All paths/arguments travel as values, not shell command text.
The core parses business options once; adapters handle shell-specific spelling and
session/terminal behavior. There is exactly one final outcome per accepted request.

Simple commands use one request and one result. The internal protocol can deliver
typed progress, warning, question, and final-result events on stdout as NDJSON.
Adapters consume events; events do not leak into public machine pipelines. Stderr
is transport/process diagnostics, not a second public data channel. Human rendering
belongs to adapters. The ordinary CLI exposes no runtime/architecture detail.

For the new executable directly: exit 0 means success, 1 means an unsuccessful
domain operation, and 2 means an invalid request/protocol/bootstrap invocation.
Adapters must parse structured failures even with a nonzero exit; a valid domain
error must not become a generic process failure. The existing PS bridge's exit-0
contract remains unchanged until its consumers are replaced together.

The direct core is machine-oriented. A separate human frontend for cmd is not in
the first scope; do not claim it has native R3CLI human output.

## Deliberate changes and guardrails

| Change | Decision and rationale |
|---|---|
| Nu runtime | Migrated commands invoke the compiled core, not PowerShell. One engine owns each invocation. |
| Result generation | Domain results are produced independently of renderer callbacks. Both adapters map these results to their existing public contracts. |
| Broken theme | Machine-only core operations work without R3CLI or theme files. Human-mode rendering still reports a presentation failure. This intentionally removes the current machine/presentation dependency. |
| Error structure | Internal message/details/hint are separate; adapters reproduce the legacy error contract and format. Any public wording changes require an explicit recorded exception. |
| Plan identity | Bind preview to project identity, source fingerprint, and intended changes. Reject an invalidated plan and ask for a new preview. No unsafe reuse after another writer changed relevant inputs. |
| Distribution | Separate source checkout, installed application, small adapter modules, and configuration/cache/journals. Moving source or changing Documents must not break the installed core. |
| Performance | Remove repeated work and shell overhead without weakening validation. Health fingerprints can exclude `dist` before hashing because it is already excluded from that health fingerprint; transaction fingerprints remain complete. |

`content list` is read-only with respect to the pack but may update reference and
health caches. `--dry-run` may populate downloads/caches as currently documented;
it must not apply planned pack changes. Do not promise a completely write-free
operation where compatibility requires auxiliary state.

PSD1 is data, not executable configuration. Preserve the accepted data subset:
tables, quoted keys/strings, booleans, null, numeric values, nested tables/arrays,
comments, whitespace, and escaped quotes used by real descriptors/metadata.
First read original files without conversion. Unknown constructs fail explicitly;
do not launch PowerShell to interpret them in the compiled core. Unsupported
previously accepted real files are compatibility failures, not silently ignored.

Keep the existing persistent schemas and paths during initial read-only coexistence.
Both implementations use the same reference-cache meaning. Pending legacy journals
must be recovered by their owning implementation or explicitly proven readable by
the replacement before mutation ownership switches. Never alternate journal writers.

## Equality and exceptions

- Public JSON: compare full values and types; ignore object-key order only.
- Ordered arrays and numbered references: compare exactly; do not normalize away
  sort/collation differences between runtimes or shells.
- Resolver: compare selected IDs/versions, intent, plan, and diagnostics on frozen inputs.
- Untouched files: byte equality. Touched files: byte equality first; a semantic-only
  exception needs a documented reason, affected case, and explicit acceptance.
- MRPack: compare entries, metadata, hashes, and semantic content; ZIP timestamps
  and compression bytes can differ without changing the result.
- Normalize only declared variable fields: temporary fixture root, generated
  request ID, time, or measured duration. Preserve real path distinctions.
- A known legacy defect is recorded separately; do not blindly preserve it or
  silently fix it as part of a supposedly identical port.

Acceptance is demonstrated with [the comparison matrix](comparison-matrix.md),
not inferred from compilation or from an unrelated legacy test run.
