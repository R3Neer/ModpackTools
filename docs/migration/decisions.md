# Decisions before implementation

Status: selected design; implementation authorized on 2026-10-12.
Evidence date: 2026-10-12. Baseline: `025b7f3` / declared 4.0.2.

## D01: C# / .NET 10, Native AOT, Windows x64 first

Choose C# with Native AOT for the installed core. JIT/framework-dependent or
self-contained JIT publication is not a shipping fallback. The user's requirement
explicitly excludes choosing C# based on a JIT comparison.

The completed startup comparison uses optimized Rust, Go, and published C# AOT
executables; [performance.md](performance.md) holds exact samples and limits.
Startup is of the same order in all three. Rust wins the diagnostic hashing
workload, but this is a small absolute difference compared with seconds in the
legacy bridge/command path. No claim that C# is fastest or that any diagnostic is
a ported inventory/resolver benchmark.

C# preserves more useful building blocks: .NET filesystem/ZIP/crypto/culture
semantics, the existing C# Maven comparator, and the existing TOML parser.
PowerShell itself is not embedded or invoked by the new core. Choosing C# does
not automatically convert the current dynamic PowerShell logic.

Rust remains a strong alternative, with a working 1.97.1 GNU toolchain/linker and
lower measured diagnostic times. Its cost includes replacing the TOML library,
porting Maven ordering and .NET behaviors, and reproducing culture/case handling.
Go also starts quickly and compiles locally, but offers no comparable reuse of
these existing building blocks. Python/Nu retain runtime dependence and are not
selected as the business engine. R3CLI belongs to the adapters in every option.

Reconsider D01 only if a real vertical slice cannot meet performance/compatibility
gates or exposes an unavoidable AOT dependency restriction. Do not restart a
language contest at every helper implementation or silently fall back to JIT.

## D02: toolchain and publication

Pin the existing SDK `10.0.300` for implementation (exact initial SDK, no unreviewed
roll-forward). Target `net10.0`, use the SDK's default supported C# version, and
publish `win-x64` with `PublishAot=true`, self-contained native output. Record the
resolved runtime/ILCompiler packs in build evidence and dependency locks. This
evaluation resolved 10.0.8 packs; compiler-pack freshness/security policy requires
an intentional validated update before a public release, not an untracked drift.

Windows C++ tools and SDK are installed and successfully linked the probe, even
though vswhere reports an incomplete installation record. Do not declare the
toolchain missing solely from that registration state. Build/package CI must run
the actual native publish and execute its artifact. No compiler/runtime/source
checkout is required on the installation destination.

Enable nullable checking, deterministic builds, explicit JSON metadata generation,
and AOT/trimming analyzers. Reject new warnings and dynamic-code requirements.
No `Assembly.Load`, `Reflection.Emit`, PowerShell hosting, dynamic plugin loader,
or reflection-based object serialization in the core.

## D03: dependencies and data formats

| Concern | Selected approach | Rationale / limit |
|---|---|---|
| JSON | `System.Text.Json`, typed DTOs and source-generated metadata; explicit `JsonElement`/writer for genuinely extensible values. | AOT-compatible serialization; no reflection fallback. |
| TOML | Tomlyn 0.19.0, same assembly hash/license as legacy, syntax-tree API with explicit domain extraction. | AOT probe parsed/round-tripped the real pack.toml without trim warnings. `ToModel` reaches reflection warnings and is excluded from the selected API path. |
| PSD1 | Dedicated data-only tokenizer/parser for the documented/real-file subset; no PowerShell package or process. | Retain originals and runtime independence. Reject unknown constructs explicitly; compare every real file fixture. |
| Maven ranges | Reuse/adapt `Private/MavenVersion.cs`, keeping its original range/qualifier fixtures. | Avoid a second guessed comparator. Nullable/style adaptation must not change ordering. |
| ZIP/JAR | `System.IO.Compression`, bounded streams and existing manifest/nesting limits. | Existing runtime APIs; no mod execution. |
| Hashes | .NET cryptography APIs for declared algorithms; buffer/stream measurement, full verification at mutation boundaries. | Remove interpreter overhead without weakening integrity. |
| HTTP | Invocation-owned reusable metadata and artifact clients; explicit timeouts, cancellation, bounded concurrency and request/identity coordination. | Keep distinct JSON/body and artifact/hash budgets; avoid a client per helper/file and unbounded task fan-out. |
| Locks/atomic writes | .NET file sharing/handles and same-directory temporary replacement; preserve existing rollback semantics. | Start from known Windows behavior; injected failure tests govern changes. |
| Tests | Existing Pester/Nu as legacy/public-contract harness; add a native-process differential harness before switching commands. | No external unit-test framework is required to begin. Managed unit tests can supplement but never replace native artifact checks. |

Initially reuse the existing checked Tomlyn artifact at build time, referenced
statically. Do not load that DLL dynamically from an installed source directory.
Its compiled code belongs in the published native artifact; package its license.

### Cache decoder refinement, 2026-10-13

Use the installed Newtonsoft.Json 13.0.4 token reader for legacy reference-cache
syntax/date recognition, statically linked into AOT. Pin assembly SHA-256
2B6FDDE54CC2D2E9277D458A42FDF9FCA30FC4299652E97144A31C90FEC91829,
product revision 4e13299d4b0ec96bd4df9954ef646bd2d1b5bf2a and its MIT notice.
It is a build input, never loaded from a PowerShell installation at runtime.
JsonTextReader and primitive JsonTextWriter Date-constructor conversion published
and executed with zero AOT/trimming warnings in isolated probes. No JsonSerializer,
reflection-based object conversion or PowerShell hosting is used.

The Newtonsoft JToken graph probe produced IL2104/IL3053 warnings and is rejected,
without suppressions or a JIT fallback. A separate probe confirms explicit built-in
System.Text.Json.Nodes primitive factories/value extraction publish warning-free.
Use this bounded temporary decoder representation, then owned JsonElement plus
typed deferred fields. Public DTO serialization remains source-generated. Date
tokens retain typed metadata; original strings are never guessed into dates later.

This replaces a growing handwritten JSON dialect/date clone that introduced
successful-behavior mismatches. The existing parser reproduces its 380-character
integer limit before BigInteger conversion. Actual byte/depth/normalization/token/
node limits remain ours. The temporary graph is not a domain inventory or a
general object adapter. Only the demonstrated safe token API subset is authorized.
Any move to a newer parser or package changes the dependency baseline and requires
separate C11/C12/round-trip evidence. Preserve `dependencies.psd1` until replacing
the legacy distribution is authorized.

## D04: one core, small native-shell adapters

Planned projects: `src/ModpackTools.Core` (business logic),
`src/ModpackTools.Cli` (machine protocol/host), and `tests/compatibility` (fixtures
and differential runs). The user has now authorized creating these projects.
Keep domain models independent of console, global shell state, and renderer callbacks.
No background daemon/service initially: one core process per invocation, explicit
per-process caches, and persisted caches with defined invalidation.

The new PowerShell and Nu adapters share the core. Partial migration uses an
explicit dispatch capability table: a command is owned entirely by the core or
the legacy implementation. Nu may still invoke PS for unmigrated commands during
coexistence; do not claim full independence before the final switch. Cross-command
numbered references and journal ownership must remain compatible.

The direct core is machine-only initially. Interactive questions are protocol
events answered by the adapter on stdin; request data travels by argument/file,
not by consuming the answer channel. Closed stdin, noninteractive invocation,
EOF, timeout/cancellation, and --yes have explicit behavior. No implicit approval
in machine-only mode. Preserve current per-command prompt policy.

## D05: R3CLI revisions

Keep legacy engine on its verified R3CLI 0.4.1 package. Build the new paired
adapters from the common R3CLI source revision
`18df65b7eff51bc0c893ec687161654ea378ee7f`, whose Python project declares 0.5.2
and PowerShell manifest declares 0.5.0/API 1. These are separate artifact version
labels; do not rewrite them to manufacture a common version.

Use the source build workflow for packaged Nu resources/license/hashes and the
PowerShell artifact from the same revision. Never depend on repository-relative
fallback resources at runtime. Theme extensions, help, widths, ASCII/NO_COLOR,
and redirection must pass C09 before switching human presentation. This evaluation
verified source/revision/API existence, not complete visual parity of the pair.

## D06: performance policy

Use the targets in [performance.md](performance.md) as acceptance gates, not
promises. Measure in an already running shell and the core directly, separately
from starting a new shell. Remove eager presentation work in machine-only mode,
reuse inventories/parsed manifests per invocation, deduplicate provider work, and
exclude irrelevant `dist` before HEALTH hashing. Do not exclude it from the
transaction's protected source tree or trust only timestamp/size for strong hash
validation. Use bounded parallelism only where measurements and ordering permit it.

Freshness must remain truthful. A faster path must report uncertainty rather than
reuse unproven health. A daemon, timestamp-only fingerprint, or weaker rollback is
not an accepted performance shortcut. Network time is measured separately; no
language claim can promise a faster remote service.

## D07: locations and update ownership

Source development remains in this isolated branch checkout. On2026-10-14 the
separate reorganization agent completed the user-authorized physical relocation:
primary repository is `D:\Juegos\Minecraft\Herramientas\ModpackTools`, main025b7f3;
linked worktree stays `C:\Users\Usuario\Desktop\ReorganizacionPC\work\ModpackTools`
with repaired Git linkage. The legacy installation is separately located at
`D:\Desarrollo\Toolchains\PowerShell\Modules\ModpackTools` and the existing Nu
import points there. These paths were verified read-only. Preserve those changes;
do not repeat relocation or replace the live installation with the partial Native.

For the new distribution choose a per-user application root under LocalAppData,
`ModpackTools/app`, with explicit configurable install-root support (including an
application directory on D). Keep `config.psd1` and caches at their existing
configuration root initially; do not migrate configuration just to move the
executable. Native recovery uses the stable namespace `MODPACKTOOLS_STATE_HOME`
(default LocalAppData/ModpackTools/native-state), independently of configuration
relocation; pending recovery must be migrated before changing that namespace.
PS adapter module location and Nu config import are separate from
the application root. A source checkout/toolchain is never a runtime installation.

Use versioned application directories, a stable install manifest read by adapters,
and a separate updater/helper process. Verify package identity/hashes and native
smoke tests before switching the manifest, preserve the old version for rollback,
and defer cleanup when files are open. Plan the complete package: core, paired
adapters, resources, licenses, dependency hashes, installation manifest, and helper.
No public version number/tag/release or external install occurs in this phase.

## D08: baseline and documentation ownership

Keep 4.0.2 on `main` unchanged. All migration records and evaluation tooling stay
on `codex/hybrid-core`; do not merge the whole branch into main. Follow the separate
documentation commits and selective future integration in the commit policy.
Implementation was explicitly authorized after evaluation and reviewer design.

## Readiness versus future validation

Language, initial runtime/target, dependency/API choices, protocol direction,
compatibility boundary, commit policy, performance targets, and initial sequence
are selected. Building a complete PSD1 reader, proving both native R3CLI presentations,
passing C01-C28, profiling network/large JAR workloads, and validating the new updater
are implementation milestones, not unresolved pre-start architecture choices.

## Primary references

- [Native AOT restrictions and prerequisites](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [System.Text.Json source generation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation)
- [Tomlyn 0.19.0 syntax/round-trip APIs](https://github.com/xoofx/Tomlyn/tree/0.19.0)
- [Cargo release profiles](https://doc.rust-lang.org/cargo/reference/profiles.html)
- [Go runtime/GC guide](https://go.dev/doc/gc-guide)
