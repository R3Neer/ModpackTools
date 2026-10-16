# Native distribution contracts

Design only, 2026-10-16. Installer/updater/launcher are NOT implemented or accepted
by this document. This refines D07 using the independent white-box design review;
implementation must publish and test the concrete types before acceptance.

## Component boundaries

| Component | Input / result | Responsibility |
| --- | --- | --- |
| Package reader | Archive path + trusted expected SHA256 -> verified manifest and owned prepared tree | Bounded ZIP/path/PE/file validation, exact allowed contents, licenses; never run unverified payloads. |
| Version publisher | Verified tree + physical installation identity -> immutable version ID/path | Same-volume owned preparation, verification, atomic directory publication; never overwrite a version with different bytes. |
| Control transaction | Expected generation + verified version -> actual switch receipt | Durable small pointer/bootstrap publication, conflict detection, rollback; publication truth survives cleanup/UI failure. |
| Stable launcher | Verified current generation + argument vector -> child exit/stdout/stderr | Resolve once, pin that version before releasing admission ownership, launch absolute native core; no shell command strings. |
| Native setup/helper | Explicit root, package selection, operation -> installation/update/rollback receipt | Serialize identity-bound ownership, compose the other contracts, handle open executables and deferred retirement. |
| Nu bootstrap | One generated static import and matching core path | Pin a coherent complete version for the imported session, independently of later current.json changes. |
| Retirement collector | Known retired generation/tree + safe process/admission observation -> Removed or Retained(reason,path) | Delete only verified owned contents after consumers are demonstrably gone; retain on uncertainty. |

No component may infer installation from source checkouts, PSModulePath or an old
PowerShell module. Runtime needs no SDK/JIT/PowerShell/Python/R3CLI source checkout.
Python remains an existing build-only R3CLI resource generator.

## Locations and identities

- Sources stay in D:/Juegos/Minecraft/Herramientas/ModpackTools and the active
  linked worktree; neither becomes a runtime package or is relocated by setup.
- A new install must have an explicit application root. This PC's proposed
  independent root is D:/Juegos/Minecraft/Herramientas/ModpackTools-App. This is
  a proposal for later activation, not permission to install there now.
- Root contains stable native launch/setup entry points, immutable versions and
  a small control directory. Keep mutable current manifest and generated Nu
  bootstrap together under control; its transaction must not copy old binaries.
- Public Nu import is control/Nushell/modpack.nu. It imports the module from one
  immutable version and sets the core path to that exact version. Configuration
  import changes require an explicit destination; no plugins/profile rewriting.
- Configuration remains MODPACKTOOLS_CONFIG_HOME or the existing local default.
  Data/cache remain separate. Recovery uses stable MODPACKTOOLS_STATE_HOME.
- Updates recover their installed root from a verified installation manifest,
  never from HOME/Documents/Tools defaults. Development executables without a
  managed installation return an actionable not-managed result for self-update.

Version directory identity is content-derived and distinct from a public product
version. Preparing internal test packages does not invent or publish a release.
Manifest requires schema, native package identity, win-x64 architecture, product
version, content/build identity, launcher/core protocol requirements and ordered
file path/size/SHA256 entries. Reject legacy PowerShell ZIPs explicitly.

An archive's own hash list proves internal consistency, not authenticity. The
expected archive digest must come from an explicit trusted local input or the
official release channel over validated HTTPS. Apply the existing bounded HTTP
and ZIP principles; no silent trust in a downloaded hash file from an arbitrary URL.

## Publication and retirement

Prepare/verify/version-smoke before switching. Use a native helper separate from
an executable being updated. Keep old/new generations available through rollback.
Pointer and bootstrap are separate files: do NOT claim joint atomic visibility.
Each consumer is independently coherent: launcher resolves one current version;
bootstrap statically binds its module, executable and resources to one version.
Use the existing journalled transaction for the small control tree and return
committed generation truth before any optional cleanup.

PID plus process creation time does not fix the race between reading an old Nu
import and registering a later lease. Do not implement unsafe lease-only GC.
The selected conservative policy publishes new bootstrap/current first, then
takes its process census under the same ownership that serializes update,
rollback, cleanup and launcher admission. Protect launchers that resolved an old
version but have not yet started its executable. A launcher pin must precede that
admission release; checking only already running old cores is insufficient.

Supported Nu processes that may retain modules, any uncertain process identity,
active version admissions and open old binaries prevent retirement. Keep an
explicit pending-retirement entry and actionable cleanup command; do not close
unrelated sessions, alter ACLs, create permanent junctions or retain an untracked
old installation just in case. Cleanup can be deliberately conservative about
other Nu sessions. A new session after publication uses the new public bootstrap.
Direct imports of retired internal version paths are outside the supported GC
contract. Collector must revalidate physical identity and complete known contents;
foreign/changed contents are retained with exact paths, never recursively erased.

## Required acceptance

Test explicit roots with spaces, clean shell profiles, full Nu workflow, native
install and update, downgrade/legacy rejection, failed preparation/smoke/switch,
rollback, current executable open and Nu session open. All packages/config/state
and projects are isolated; no real modpacks or external publication.
Verify receipts, actual locations, hashes, fresh session resolution and absence of
Documents/Tools/legacy dependencies. Prove retention while a consumer is open.
After closing the owned consumer, complete cleanup only if no other supported Nu
session prevents proof; otherwise explicitly report Retained/coverage limitation,
never close foreign processes or force a green result. Pending live activation and
the reorganization handoff remain separate from implementing this distribution.
