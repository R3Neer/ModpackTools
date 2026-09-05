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
own presentation context, removes `--colour` and `--ascii` once, and passes the
remaining arguments to the original command parser. Help runs before project or
provider access. Version output is a single plain line. No JSON mode is added.

## Streams and errors

Presentation uses information stream 6; warnings use stream 3. Neither creates
success-pipeline objects. `Throw-MpError` retains its namespaced ID, category,
target and terminating behaviour; R3CLI formats the message without emitting a
second diagnostic. Unexpected exceptions remain visible.

Removal help is another entry in the same command catalogue. Removal plans pass
literal names and reasons (requested, dependent, unused dependency) to the shared
status renderer. File previews use the common transaction summary, and cancellation
uses an information status. Confirmation follows the existing prompt convention
with a default of no; `--yes` and `--dry-run` do not prompt.

`--colour always` generates ANSI in the information records. PowerShell's host
and downstream formatters can remove those sequences according to their own
`OutputRendering` preference. Inspect `InformationRecord.MessageData` to capture
the renderer's original text; ModpackTools never changes the global preference.
Automatic rendering is plain for redirected output. ASCII changes presentation
symbols, not names or other user-supplied text.

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
```

The integration checks fresh-process help and errors, custom-theme preservation,
rejection of a corrupt upgrade with the installed bytes unchanged, and redirected
output. It isolates PSModulePath inside child processes so it cannot accidentally
select or overwrite the user's normal installation. The optional existing live
Packwiz scenario also accepts `-ModulePath` to verify this installed artifact.
