# R3CLI integration

ModpackTools consumes the official PowerShell adapter from R3CLI. The revision,
version and SHA256 hashes in `dependencies.psd1` identify the exact package under
`Private/vendor/R3CLI`; it is loaded from that path rather than PSModulePath.
The module remains private and ModpackTools exports only `modpack`.

## Responsibilities

R3CLI owns banners, sections, status symbols, layout, help presentation, the
canonical palette and diagnostic formatting. ModpackTools owns its command
catalogue, selection rules, domain data and composition of inventory/build/doctor
views. Rows pass literal text segments and semantic roles to R3CLI. The product
contains no common ANSI palette or copy of the generic renderer.

Doctor and inventory pass each known dependency conflict as its own status item.
Doctor consolidates optional recommendations across client and server, groups them
by owning mod, and groups incomplete verification by structured cause. The default
view stays bounded while `doctor --details` expands every individual incomplete
result. A separate build-artifact section reports whether the newest `.mrpack`
matches the current project and gives a direct rebuild instruction when stale.
Incomplete coverage also states that Fabric Loader may still reject startup. Items
are never joined into a single renderer line.

The existing catalogue is projected into R3CLI's `HelpCatalogue` shape without
creating a second documentation catalogue. Each public invocation constructs its
own presentation context, removes `--color`, `--ascii`, global `--project`, and
machine-output options once, then passes the remaining arguments to the domain
command parser. Help runs before project or provider access. Version output is a
single local plain line without network access.

## Human and machine channels

Machine-readable output is additive. `--json` emits one schema-versioned JSON
envelope on stdout while retaining the normal R3CLI presentation. In that mode the
human presentation is routed to stderr so stdout remains valid JSON for native
callers. `--json --no-human` suppresses the presentation entirely and leaves only
the JSON envelope. `--no-human` without `--json` is rejected.

Machine DTOs are snapshots of domain objects, not serialized renderer text. The
contract uses stable lower-case field names and records command identity, arguments,
success state and structured data. Expected failures produce a matching error
envelope before the normal terminating PowerShell error is preserved. The Nushell
bridge consumes that envelope and converts failures into native Nu errors.

The Nushell adapter always requests JSON internally. Human R3CLI output therefore
remains visible on stderr while the parsed value is returned through the Nushell
pipeline. Commands such as `content list`, `content search`, `content versions`,
and `project list` unwrap their common collection directly; status and other
compound results remain records. Human text never becomes the returned Nu value
and therefore never becomes `$ans.last`.

## Streams and errors

Outside JSON mode, presentation uses information stream 6 and warnings use stream
3. Neither creates success-pipeline objects. In JSON mode a R3CLI sink writes the
human presentation to stderr instead. `Throw-MpError` retains its namespaced ID,
category, target and terminating behaviour; R3CLI formats the message without
emitting a second diagnostic. Unexpected exceptions remain visible.

Removal help is another entry in the same command catalogue. Removal plans pass
literal names and reasons (requested, dependent, unused dependency) to the shared
status renderer. File previews use the common transaction summary, and cancellation
uses an information status. Confirmation follows the existing prompt convention
with a default of no; `--yes` and `--dry-run` do not prompt.

`--color always` generates ANSI in human presentation. PowerShell's host and
downstream formatters can remove those sequences according to their own
`OutputRendering` preference. In JSON mode R3CLI terminal detection is based on
stderr, because stdout is reserved for the machine envelope. ASCII changes
presentation symbols, not names or other user-supplied text.

## Theme compatibility and installation

`theme.toml` uses `[colours]` and only declares client, host and local by default.
Other roles inherit R3CLI unless explicitly overridden. Legacy `[colors]` is
normalised as data at read time; it is never rewritten just to render output.
An unchanged common role is inherited and a different value remains an override.

The installer verifies R3CLI, imports the staged module and validates the theme
before replacing an existing installation. A customised installed theme wins over
the incoming theme and is copied byte-for-byte. To deliberately reset a custom
theme, back it up and replace it with the source theme. Incomplete/corrupt packages
fail before project mutations and identify the installer as the recovery path.
Bootstrap under Windows PowerShell 5.1 uses minimal plain text until PowerShell 7
and the verified renderer are available.

When Nushell is available on PATH, the installer also writes an idempotent marked
block to the user's `config.nu` that imports the installed `Nushell/modpack.nu`
module. The adapter invokes the installed PowerShell module through
`Invoke-ModpackBridge.ps1`; PowerShell remains the canonical engine and parser.
The active-project session state crosses child PowerShell processes through the
`MODPACKTOOLS_PROJECT` environment variable.

## Updating the dependency

Changes to general presentation belong in R3CLI. Implement and commit them there,
then run the development-only update command from the ModpackTools repository:

```console
python scripts/update_r3cli.py <clean-R3CLI-checkout>
```

The script refuses dirty source by default, builds the upstream package and
records its commit and hashes. Its `--allow-dirty` option is only for local
iteration; regenerate from a clean commit before delivery. Do not hand-edit
vendor files. Python 3.11+ is needed for this maintainer step, not installation
or runtime. The upstream build normalises text to LF for reproducible hashes.
The bundled MIT licence remains with the dependency.

## Verification

Run the complete Pester 4.10.1 suite, then the disposable installation scenario:

```powershell
Invoke-Pester -Script ./Tests
./Tests/Invoke-PresentationIntegration.ps1 -WorkRoot <scratch-directory>
./Tests/Invoke-SelfUpdateIntegration.ps1 -WorkRoot <scratch-directory>
```

CI also parses the Nushell adapter with `nu-check --as-module` under Nu 0.115.1 and
executes the bridge contract. The machine-output Pester tests verify the JSON
option parser, clean success/error envelopes, DTO snapshots and transaction data.

The integration checks fresh-process help and errors, custom-theme preservation,
rejection of a corrupt upgrade with the installed bytes unchanged, and redirected
output. It isolates PSModulePath inside child processes so it cannot accidentally
select or overwrite the user's normal installation. The optional existing live
Packwiz scenario also accepts `-ModulePath` to verify this installed artifact.

`self-update` is an executable entry in the same help catalogue and has a detailed
help page. Its release check, plan, cancellation and completion use the shared
renderer. `--version` never reads the update cache or contacts GitHub;
`self-update --check` raises a namespaced diagnostic if its explicit query fails.
The version line reports loaded code, which can remain older until a new
PowerShell session is opened.

`Installation.ps1` centralizes user target selection and child-process execution
for the installer and updater. The installer verifies the newly placed package
through `VerifyInstallation.ps1` before deleting its backup. The self-update
integration substitutes HTTP transport only: real ZIP verification, an old loaded
module, the real installer, custom-theme preservation, fresh-process validation
and rollback after an injected post-replacement failure are exercised in isolation.
