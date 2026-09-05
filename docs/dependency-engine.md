# Dependency engine and project transactions

The public CLI reads project state, resolves intent, validates the resulting graph,
freezes a plan, and commits the prepared files. Only `modpack` is exported.

## Commands

```powershell
modpack add sodium lithium --category performance --dry-run
modpack add sodium lithium --category performance
modpack update sodium --allow-downgrade
modpack update sodium --to <exact-version-id>
modpack pin sodium lithium
modpack unpin sodium
modpack classify set sodium lithium performance
modpack side set sodium iris client
modpack classify remove performance visuals --unclassify
modpack resource disable A B
modpack resource move C A B --position 2
modpack add <resource-pack-slug> --enable --position 1
modpack inventory --check
modpack build --strict
modpack doctor --project my-pack
modpack doctor --project my-pack --details
modpack doctor --fix --dry-run
modpack doctor --fix --yes --allow-downgrade
```

Every project mutation above supports `--dry-run`. Classification supports it for
`set` and `remove`. Category and side are the last positional argument of their
respective batch commands. Resolve all numbered selectors before preparing changes;
search numbers, inventory numbers and category numbers keep separate scopes.

Resource blocks are removed from the active list before insertion. The requested
position is one-based in that remaining list; the argument order is preserved.
Duplicate selectors resolve to one entry. `move` requires all targets to be enabled;
`enable` also repositions enabled targets. Disabling an already disabled pack is a
no-op. Default Options is required for activation and ordering.

`add --category` only classifies the explicitly requested mods. It rejects mixed
content batches. `--enable` and `--position` must be supplied together, with only
resource packs as explicit targets. The complete add/activation is one transaction.

## Resolution and validation policy

New requirements come from Modrinth and the actual JAR manifests. A provider project
ID, a loader mod ID and a loader version are separate identities. Unknown mod IDs
are never installed by fuzzy name search. Existing local files participate in
validation but cannot be automatically replaced.

The requested version is fixed; absent an exact request, the newest compatible
publication is selected. Dependency search backtracks across the affected managed
projects and handles required cycles. It minimizes changed installed items, then
changed explicit items, then additions. Publication timestamps break ties, with
stable ID ordering. It does not remove content or migrate Minecraft, the loader or
Java. Search stops with a diagnostic, without committing, after 10000 distinct
states rather than silently returning an unproven solution.

Automatic downgrades require `--allow-downgrade`. An exact `update --to` is already
explicit authorization for the selected target version. Numeric `--to` resolves
against the saved version list before comparing its stable ID against current
compatible versions; resolution does not silently renumber that list.

Packwiz's `pin` field in each `.pw.toml` is the only pin source of truth. `--all`
skips pins; explicitly changing a pin fails with an `unpin` hint. Intent is stored
in `.modpack/metadata.psd1` as `ContentSchemaVersion = 1` and a `Content` table keyed
by stable content IDs, with `Intent = 'explicit'` or `'transitive'`. Older entries
are explicit by default. Adding an installed transitive explicitly promotes it.
Orphan removal is opt-in through `remove --autoremove` and limited to dependencies
reachable from the requested/cascaded removals. Explicit content, local artifacts,
pins and unrelated old orphans become retention roots. Reachability retains shared
dependencies and conservatively retains all alternatives and conditional guards;
unreachable transitive cycles can be removed together. Incomplete baseline
verification blocks autoremove instead of guessing about hidden references.

`Remove.ps1` resolves the whole installed batch, subtracts the requested nodes and
uses `Get-MpGraphReport` to detect newly broken requirements. `--cascade` repeatedly
subtracts their owners until stable. It never invokes the add/update resolver to
reinstall deleted content or change a surviving version. The final graph uses the
same baseline/strict policy as other content operations. Pins block explicit and
cascaded removal. A staged inventory and graph verify materialization before commit.

The CLI freezes selectors to IDs before preview. It displays the plan with R3CLI,
confirms with a default of no (or `--yes`), and compares the prepared file hashes
with the preview before applying. `--dry-run` does not prompt or mutate the project.
Removal cleans content-specific editorial entries and active resource references,
preserving categories, configs, worlds and remaining Default Options order.

Without `--strict`, add/update permit unchanged, unrelated baseline conflicts,
but block conflicts introduced by changed requirements or affected versions.
Build blocks every known required conflict, including with `--no-refresh`.
Unverifiable metadata is reported separately. `--strict` additionally requires a
clean graph with complete verification; optional recommendations remain warnings.
Doctor and build apply the same provider-availability normalization, so a provider
with no verifiable candidate cannot become a build-only hard conflict. It remains
incomplete verification and blocks only under `--strict`.

Fabric, Quilt, Forge and NeoForge manifests are read without executing mod code.
Supported metadata includes ranges, aliases, nested JARs, multiple mods per file,
client/server scopes, Quilt alternatives and conditions, Forge mandatory/optional
dependencies, and NeoForge incompatible/discouraged dependencies. Maven ranges use
a separate comparator. TOML manifests use the bundled Tomlyn 0.19.0 parser; its
assembly hash and BSD license are recorded with the dependency.

Declare the pack's target Java version with optional `JavaVersion = '21'` in
`.modpack/project.psd1`. The locally installed Java is not assumed to be the target.
Missing runtime information, unsupported schema/expressions, load ordering,
ambiguous bundled-version selection, Jar-in-Jar library negotiation and dependency
override files produce incomplete verification rather than a claim of health.
Read limits are 4 MiB per manifest, 128 MiB per nested JAR and 16 nesting levels.
This is a declared-dependency check, not a Minecraft launch or mixin compatibility
test. Automatic acquisition of new dependencies uses Modrinth. Other Packwiz
providers retain staged updates with validation of the prepared result.

## Transaction implementation

`Transaction.ps1` owns staging, the project lock, fingerprints and recovery.
`LoaderMetadata.ps1`, `Graph.ps1` and `Resolver.ps1` own parsing, diagnostics and
resolution; `Operations.ps1` and `Batch.ps1` connect the command handlers.
`Health.ps1` serves inventory, build and doctor from the same graph report. Graph
issues carry structured cause codes so presentation can consolidate sides, group
recommendations and explain incomplete verification without parsing message text.

Staging and journals live under the ModpackTools configuration directory, outside
the pack. The source tree is copied without `.git`; linked paths are rejected.
The transaction fingerprints the source before preparation and again before commit,
and checks individual destinations while committing. It never resets Git state.
Modrinth technical metadata is written from the frozen version/file/hash plan,
preserving unrelated sections, sides and pins. Packwiz refreshes the index and
exports from staging. It never selects another Modrinth version during commit.

Before the first project write, the journal records before/after hashes and backup
files. A failed commit restores affected files and removes files and empty
directories created by that transaction. An interrupted pending journal is
recovered under the project lock before the next mutation. Recovery preserves an
unexpected external edit and reports its journal rather than overwriting it.
This provides rollback/recovery, not simultaneous visibility of multiple filesystem
renames to unrelated processes. A pending conflicted journal requires reconciliation.

Build preserves the previous artifact until the staged export succeeds. Before
replacement, it compares that artifact semantically with a fresh export of the
prepared project and rejects any mismatch. Doctor reports the newest artifact as
missing, current, stale or unverifiable; a stale artifact is explicitly unsafe to
install until a new build succeeds. The freshness cache combines the source-tree
fingerprint, artifact hash and validator revision. Doctor's
environment repair remains separate. Project repair previews the file plan, uses
the existing confirmation/`--yes` when the plan contains changes, and rejects a
changed plan before applying it. Empty plans finish without prompting or running a
second transaction.
It regenerates the index and resolves repairable required dependencies without
guessing categories or rewriting corrupt editorial metadata.

## Cache and testing

Artifact caches are keyed by declared hash and checked on reuse. Metadata and health
snapshots expire after 24 hours. Health also depends on a fingerprint of project
files and the validator revision; stale health is not reused after project changes.
Normal inventory uses local artifacts and available cache without network calls.
`inventory --check`, content resolution, build and doctor request verification.
Downloads and caches may change during a dry run; project files do not.

Run the deterministic suite with PowerShell 7 and Pester 4.10.1:

```powershell
Import-Module Pester -RequiredVersion 4.10.1
Invoke-Pester -Script ./Tests
```

The optional live integration creates its own project and cache beneath a supplied
directory, downloads actual Modrinth artifacts, tests dry-run/add/pin, and inspects
the exported MRPack. It never selects a user's existing pack:

```powershell
./Tests/Invoke-LiveIntegration.ps1 -WorkRoot <scratch-directory> -PackwizPath <packwiz.exe>
```
