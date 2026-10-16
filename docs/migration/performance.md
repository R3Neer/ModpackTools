# Performance evaluation and acceptance targets

Initial baseline date: 2026-10-12. The baseline sections remain historical;
the current implemented-package measurements below have their own scope.

## Installed-distribution closure, 2026-10-16

Final Native AOT CLI196/package build0D475640 is compared with unchanged main
025b7f3 through their actual imported Nu commands in one running Nu0.116.2 process.
31 measured samples per engine/workload plus separately retained first observation;
engine order alternates. Both use the same copied project and separate private
configuration/cache roots. Raw health preflight confirms20 local or100 managed
mods and zero errors/warnings/unknown for both engines. Copied provider metadata
mtimes are refreshed to exercise fresh cached verification; original fixtures are
unchanged. Managed JARs contain256KiB deterministic payload each, with SHA512 verified.

| Public Nu operation | Legacy median / p95 ms | Native median / p95 ms | Median ratio |
|---|---:|---:|---:|
| Version, machine | 941.65 /1036.78 | 42.82 /54.53 | 21.99x |
| Inventory20 local, machine | 1473.36 /1569.37 | 104.14 /133.65 | 14.15x |
| Inventory100 managed, machine | 2051.78 /2176.06 | 300.34 /331.69 | 6.83x |
| Inventory20 local, human | 1474.21 /1607.49 | 273.49 /417.65 | 5.39x |

These are complete command times, including child process startup, request/result
validation, cache IO and human rendering where named. Machine Nu values compare
canonically in full before the series and remain unchanged in each sample. Human
commands return null: that equality is not a comparison of rendered text. Functional
presentation acceptance is covered separately. Output is redirected, ASCII/color
never, COLUMNS80; captured human stderr is retained outside Git.

Separate31-sample fresh-process version measurements, including PowerShell test
harness setup: core median22.98/p9532.52ms; stable installed launcher median226.07/
p95246.61ms. The stable path includes full-package hashing, admission, pinning
and helper delegation. These costs were retained for integrity; no medians are
subtracted to invent a core-only duration.

Measured Nu latency/ratio thresholds and direct-version threshold are met on these
synthetic fixtures. This does not certify every original target or workload:
standalone core inventory, large/nested graphs, transaction phase scaling and live
process-tree memory were not remeasured here. Earlier focused data remains below.
OS caches were warmed by copying/hashing; even sample0 is not physical-disk cold.
No network verification was requested. This is a shared PC, not a dedicated host;
reviewer runtime tests and SDK builds were idle during timing. One small progress
snapshot read occurred during the series; no competing test/build was launched.

All package/project/original fixture manifests and legacy source anchors remain
unchanged. First preflight failed because an internal fixture lacked public
metadata.psd1; the new copied fixture adds that file before its baseline snapshot.
Failed run is retained, no product changes or discarded slow timing series.

Reproducer: [PS driver](benchmarks/Measure-NativePublic.ps1),
[Nu driver](benchmarks/Measure-NativePublic.nu). The Nu imports are deliberately
bound to this environment's legacy source and frozen final package; the PS driver
rejects a different package. Raw samples, preflight coverage, hashes and manifests:
[public-native-results.json](benchmarks/public-native-results.json).
Additional raw envelopes/stdout/stderr and failed preflight are under
assessment/benchmarks/public-native-759bafc4848148dfab7791852f9c8c30 and
public-native-83c0772fc2e84ef8a5b50fe42e41ce39.

## Implemented raw inventory checkpoint, 2026-10-13

The actual Native AOT reader from b603678 was measured against the unchanged
025b7f3 module on an isolated copy of the relevant real-pack files: 123 files,
6731513 bytes; 100 mods, 17 physical resources, 25 active rows, zero inactive rows
and two shaders. Full common project/metadata/item projections matched before the
timing series. Source hashes and attributes remained unchanged. No network,
health, reference cache, renderer or public adapter was exercised.

| Engine / measured scope | Median ms | p95 ms | n |
|---|---:|---:|---:|
| Already-loaded PowerShell: read, equivalent projection and JSON | 153.17 | 168.29 | 21 |
| Fresh Native AOT inspector: launch, read, projection and JSON | 64.33 | 66.93 | 21 |

The observed median ratio is 2.38x. Native also emits original JAR observations.
Samples alternate which engine runs first. Copying and first observations warm OS
caches; no eviction or disk-cold claim. First observations, excluded from the
series: PS 437.88 ms, native 88.43 ms. No competing agent tests/builds were
intentionally started during this short local run; it is not a dedicated host.

This establishes a faster implemented reader despite native process overhead.
It does not establish the complete public inventory's >=4x median gate: health,
references, rendering and adapter costs remain to be implemented/measured. Do not
subtract separate startup medians to claim a reader-only native time, or extrapolate
this metadata-oriented workload to a large installed JAR collection/resolver.
Reproducer: [Measure-RawInventory.ps1](benchmarks/Measure-RawInventory.ps1).
Raw samples, environment, executable SHA and fixture manifest:
[raw-inventory-results.json](benchmarks/raw-inventory-results.json).

## Project health composition checkpoint, 2026-10-14

Health60 (d0f2ee9) was measured on synthetic Fabric fixtures against legacy
025b7f3. Both build inventory and the provider-validated dependency report. The
loaded PowerShell module emits a common projection; a fresh Native AOT inspector
also emits richer node/version/file data. This includes native process startup
and serialization. No startup medians were subtracted.

| Fixture / engine | Median ms | p95 ms | n |
|---|---:|---:|---:|
| 20 local JARs / loaded PowerShell | 78.6119 | 88.6501 | 21 |
| 20 local JARs / fresh Native AOT | 56.6265 | 66.2016 | 21 |
| 100 managed, verified cached JARs / loaded PowerShell | 529.7347 | 660.8835 | 21 |
| 100 managed, verified cached JARs / fresh Native AOT | 246.4223 | 259.6175 | 21 |

Observed median ratios are 1.39x and 2.15x. Each JAR contains 256 KiB of
deterministic padding; the managed fixture verifies SHA512. Common projections
were compared before timing; timed samples checked exit status, stderr and stable
output length, not full output equality on every run. Full fixture manifests and
four legacy source hashes remained unchanged, independently checked by white-box
review. Samples alternate engine order. Fixture creation and manifest hashing
warm OS caches; even the retained first observations are not disk-cold.

These are simple healthy graphs without nested JARs. No network, persisted health
cache/fingerprint, public host, Nushell, renderer, transaction or live-memory
measurement is included. This exploratory 21-sample series does not satisfy the
31-sample public-command acceptance gates below. It measures implemented health
composition, not complete installed-tool performance. No competing agent builds
or tests were intentionally started during the series; the host was not dedicated.

Reproducer: [Measure-ProjectHealth.ps1](benchmarks/Measure-ProjectHealth.ps1).
Raw samples, first observations, executable hash, environment and manifests:
[project-health-results.json](benchmarks/project-health-results.json).

## Original evaluation environment and workload

- Windows 11 Home 10.0.26200; Intel i7-12650H, 10 cores/16 threads; 16005 MiB RAM.
- PowerShell 7.6.6; custom Nu 0.116.2; unmodified ModpackTools `025b7f3` / 4.0.2.
- Rust 1.97.1/cargo 1.97.1, x86_64-pc-windows-gnu, optimized Release, bundled
  rust-lld/self-contained linking; Go 1.27.0 windows/amd64, default optimized build.
- C# SDK 10.0.300, net10.0, Release Native AOT win-x64, resolved compiler/runtime
  packs 10.0.8. No JIT timings are used in the language decision.
- Real workload: `D:\Minecraft\VanillaPlus-26.2`, 152 regular files excluding
  `.git`, 28,459,098 bytes total; 21,676,098 bytes are under `dist`.
- The pack has managed metadata without a representative full installed JAR
  collection. It is not a large-artifact or online-resolver benchmark.

Configuration/reference/health cache writes were isolated in evaluation directories.
The commands read the real pack; they did not mutate it or select a write command.
No network verification was requested. No global tool installation or product
installation was performed; AOT build packages were restored into the evaluation
directory. Diagnostic C#/Rust/Go programs are not the new product implementation.

## Legacy process timings (milliseconds)

One first run plus 21 measured sequential runs per case. Median is the middle
ordered sample; p95 uses nearest-rank. First-run results are retained in JSON but
are not labeled disk-cold: OS caches were not evicted.

| Operation | Median | p95 | Scope |
|---|---:|---:|---|
| Empty PowerShell | 368.46 | 434.95 | Process startup only |
| Import module | 909.92 | 1347.39 | Startup plus import |
| Public PS version | 1281.03 | 2306.80 | Startup, import, console initialization, result |
| Bridge version | 1143.69 | 1365.22 | Fresh PS bridge process |
| Bridge inventory | 3983.54 | 7254.02 | Fresh PS bridge, warm configuration cache after first run |
| Nu version | 2810.49 | 3521.00 | New Nu process and existing adapter/bridge |
| Nu inventory | 5364.77 | 5746.74 | New Nu process and existing adapter/bridge |

Do not subtract independently measured medians to claim an exact attribution.
These series were sequential and the machine was not a dedicated benchmark host;
some earlier samples overlapped diagnostic compiler work. Preserve raw spread and
repeat on a quiet host before publishing a formal performance claim.

## Already-running Nu session

One Nu process invoked version and inventory 22 times each; the first is excluded.
Configuration and OS caches were already warmed. These timings exclude starting
the host shell and are the more relevant ordinary interactive baseline.

| Operation | Median | p95 | n |
|---|---:|---:|---:|
| Version via current adapter | 2038.18 | 2234.90 | 21 |
| Inventory via current adapter | 4763.21 | 5601.84 | 21 |

## Legacy internal phases

Measured inside one imported PS process, one first observation plus six warm
observations per phase. Each health-cache miss uses a new application-cache root;
that does not flush OS caches. Warm-hit observations reuse the final generated
health snapshot. Values are diagnostic; n=6 is too small for robust tail claims.

| Phase | Median ms | Observed nearest-rank p95 ms |
|---|---:|---:|
| Local inventory construction | 319.02 | 322.15 |
| Full-tree SHA256 state | 113.23 | 118.38 |
| Health application-cache miss | 1002.07 | 1126.50 |
| Health application-cache hit | 144.72 | 165.96 |

The cache hit still traverses/hashes the tree. The source excludes `dist` only
after hashing while constructing the health fingerprint. Excluding `dist` before
HEALTH hashing can avoid 21.68 MB in this workload without changing that fingerprint.
Do not apply this exclusion to transaction integrity. Inventory also constructs
data again in other paths, saves numbered references, and invokes renderers even
when their sink discards human text. Startup, setup, rendering and duplicated
work are optimization targets; there is no evidence that hashing alone explains
the multi-second total.

## Rust / Go / C# Native AOT comparison

All probes write the same fixed JSON on startup. The hash probe walks the same
152 files, hashes all bytes with SHA256, then hashes a sorted path/hash listing.
All outputs have the identical fingerprint:
`4f6ccd99cf4a460c082ee2722f81d5d042a52e318f149b595e16f4368e6be228`.

Final source uses Tomlyn syntax API, not the reflection mapper. Measurements
were rerun after that source change. Startup: first run + 31 measured interleaved
samples; hash: first run + 21 measured interleaved samples, warm OS caches.

| Runtime | Startup median / p95 ms | Launch + tree-hash median / p95 ms | Probe EXE bytes |
|---|---:|---:|---:|
| Rust Release | 33.00 / 42.85 | 71.34 / 76.23 | 1,224,192 |
| Go | 33.70 / 42.72 | 76.72 / 81.25 | 3,065,856 |
| C# Native AOT | 39.22 / 44.56 | 113.98 / 123.19 | 1,904,128 |

C# AOT is close in startup and slower in this hash probe. That result is preserved,
not hidden. The approximately 43 ms hash-probe median gap is small against the
current 4.76-second session command, and does not outweigh C#'s reuse advantages
for the selected migration. Probe sizes are not projected product package sizes.
Different streaming libraries/defaults mean this is an application-kernel comparison,
not a controlled comparison of identical hash library internals.

The first C# exploration used JIT; the user required Native AOT and it was excluded
from the final comparison. Native AOT was published and the resulting EXE executed.
The existing Tomlyn `ToModel` route exposed reflection trimming warnings. Selecting
`Toml.Parse`/syntax round-trip removed those warnings; publish with trimming/AOT
warnings as errors and the real pack.toml/comparator probe both passed. This proves
that particular API path, not every future parser/loader behavior.

Peak-memory queries after process exit returned zero/unavailable on this host.
Treat memory as unavailable, not zero. Original raw observations remain intact;
the corrected summary explicitly uses null. Memory is a pending measurement
requirement for the implemented vertical slice, with live process-tree sampling.

## Acceptance targets (not achieved results)

On this fixed small workload, using a Release published native core, 31+ measured
samples after a separately retained warm-up, same host and cache conditions:

| Path | Initial acceptance gate |
|---|---|
| Core version, machine mode | p95 <= 100 ms including core startup |
| Version from already-running Nu/PS, machine mode | p95 <= 250 ms |
| Core inventory, local/warm caches | p95 <= 500 ms |
| Inventory from already-running Nu/PS, machine mode | p95 <= 750 ms and >= 4x median improvement over a contemporary legacy run |
| Human inventory | No dependence on PS from Nu; measure rendering separately and target p95 <= 1000 ms on the fixed pack/width |
| Large/nested JARs and offline resolution | Publish separate scaling data; >= 2x median improvement where legacy CPU overhead is dominant; no regression in chosen plan or verification |
| Transactions | No degradation of any integrity/recovery assertion; measure preparation, hash, commit and rollback independently |
| Memory | Measure live process tree; aim for <= 128 MiB core peak on the small read-only workload, without changing verification |

If a gate fails, profile the vertical slice before expanding scope. Treat these
as explicit design targets; reassess with evidence if an unavoidable workload
floor makes one unrealistic. Do not reduce correctness, fixture coverage, or
measurement honesty to meet a number. No absolute promise is made for remote
Modrinth/Packwiz/network latency or cold physical storage.

## Reproduction and evidence

The [benchmarks directory](benchmarks/README.md) contains source, runners, build
commands, and raw/summary JSON. Run evaluation tooling outside installed product
directories, with an isolated output/configuration root. Do not benchmark the
production pack with a mutation or an injected failure.

Before reporting shipping improvements add: idle-host reruns, live memory/CPU/load
sampling, known OS-cold methodology (or label unavailable), application cache miss/hit
separation, larger real JAR fixtures, replayed offline resolver/provider data, and
separate online latency. This design evaluation does not pretend those measurements
were already completed.
# First implemented capability: version

Measured 2026-10-12 with product AOT executable (not the diagnostic startup probe),
PS 7.6.6 and Nu 0.116.2, on the same host described below. Both adapters imported
once; shell startup excluded. One first/warmup + 21 measured samples per mode,
legacy first then native, no OS cache eviction, no network and no build running.
Shared interactive host; reviewers could be performing small independent tests.

| Already-running adapter | Legacy median / p95 ms | Native median / p95 ms |
|---|---:|---:|
| PS | 9.48 / 17.72 | 34.02 / 40.72 |
| Nu | 2143.64 / 2351.35 | 43.77 / 53.77 |

Native Nu median is about 49x faster on this version workload by eliminating the
PowerShell child/bootstrap path. Already-imported PS version is slower because
the old version is a tiny in-process operation and native adds process startup.
Both native adapter p95 values satisfy the 250 ms version gate. These results do
not establish inventory, resolver, memory or installed-distribution performance.
Raw samples: benchmarks/native-version-{ps,nu}.json; reproduction scripts:
benchmarks/Measure-NativeVersion.{ps1,nu}. Pass repository/EXE paths explicitly.
