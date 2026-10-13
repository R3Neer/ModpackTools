# Diagnostic benchmark sources and evidence

These files evaluate the legacy engine or standalone runtime kernels. They are
not a ModpackTools replacement, not shipped code, and must not enter `main`.
Generated executables, package caches, `bin/obj/target`, and temporary pack settings
belong in an external evaluation output directory, never the installed product.

## Build and run native probes

From PowerShell 7, point to existing compilers and choose a new output directory:

```powershell
$output = Join-Path $env:TEMP ('modpack-evaluation-' + [guid]::NewGuid().ToString('N'))
./Build-Probes.ps1 -OutputRoot $output `
    -RustcPath 'D:/Desarrollo/Toolchains/Rust/cargo/bin/rustc.exe' `
    -CargoPath 'D:/Desarrollo/Toolchains/Rust/cargo/bin/cargo.exe' `
    -RustupHome 'D:/Desarrollo/Toolchains/Rust/rustup' `
    -CargoHome 'D:/Desarrollo/Toolchains/Rust/cargo' `
    -RustLinker 'D:/Desarrollo/Toolchains/Rust/rustup/toolchains/1.97.1-x86_64-pc-windows-gnu/lib/rustlib/x86_64-pc-windows-gnu/bin/rust-lld.exe'
& (Join-Path $output 'Measure-Startup.ps1') -Samples 31
& (Join-Path $output 'Measure-Hash.ps1') -ProjectRoot 'D:/Minecraft/VanillaPlus-26.2' -Samples 21
& (Join-Path $output 'dotnet-aot/Startup.exe') --parsers 'D:/Minecraft/VanillaPlus-26.2/pack.toml'
```

The builder invokes C# `dotnet publish -c Release -r win-x64 -p:PublishAot=true`,
with detailed trimming diagnostics and AOT/trimming warnings as errors. It builds
Rust Release offline with the exact Cargo.lock and Go with its optimized defaults.
It references the baseline Tomlyn DLL and Maven source statically at compile time;
the AOT EXE does not load them from a checkout when run. Nullable is disabled ONLY
in this evaluation fixture to include the unchanged legacy comparator; the planned
product enables nullable checking and adapts the comparator in a tested checkpoint.

Default .NET packs may require network restore; they are written under OutputRoot.
No global runtime/tool install is requested. The recorded local evaluation resolved
10.0.8 compiler/runtime packs using SDK 10.0.300. Record versions after any rerun.
If a build/toolchain check fails, stop; never label JIT output as AOT.

The runners validate exit status, JSON payload identity, and identical aggregate
hash output across all samples/languages. Startup includes launching an EXE and
writing fixed JSON; hash includes launch plus recursive SHA256. Neither implements
inventory, resolution, PSD1, transactions, R3CLI, or a product command.

## Legacy commands and already-running Nu

```powershell
& (Join-Path $output 'Measure-Legacy.ps1') `
    -Repository '<baseline-checkout>' -ProjectRoot '<existing-read-only-pack>' `
    -NuPath '<nu.exe>' -Samples 21
```

This records a temporary `run-*/config` root beside the copied runner. The fixture
uses project ID `vp26`; to use another pack, change the request and argument IDs
consistently and record that change. Set the configured root to the pack's parent,
as the current legacy CLI resolves registered projects there. No verification/network
switch is requested. Existing configuration is not used or overwritten.

For `Measure-NuSession.nu`, the builder replaces its compile-time adapter constant
with the chosen LegacyRoot path. Invoke the generated script using the isolated
config directory created by Measure-Legacy; capture its JSON to a separate file.
It measures elapsed time inside one Nu process, excluding host-shell startup.

## Recorded artifacts

- `legacy-raw.json`: all original legacy process/phase observations.
- `legacy-summary.json`: corrected summary; invalid post-exit memory zeros are null.
- `nu-session-results.json`: all already-running-session observations.
- `startup-results.json`: final AOT/Rust/Go startup observations, after syntax API selection.
- `hash-results.json`: final identical-workload hash observations.
- `environment.json`: hardware/toolchain/workload/build facts and evidence limitations.

Raw legacy memory observations returned 0 after process exit. Keep those historical
raw values but do not interpret them as memory use; corrected summary marks them
unavailable. The corrected runners do this directly for future runs. First samples
are not physically cold-storage measurements. Cache/host/load limitations and
pending memory/online/large-JAR measurements are detailed in [performance.md](../performance.md).

Raw inventory repeat after shared scalar compatibility (`0ba0245`, host v48):
[raw samples and manifest](raw-inventory-current-results.json). Same frozen 123-file
fixture is copied again; full common projection and original hashes/attributes pass.
21 alternating warm samples compare an already-loaded PS7.6.6 module with a fresh
Native AOT process per invocation. Median PS172.709ms / native65.6657ms, p95
182.0609ms /69.2784ms, ratio2.63x. Original v29 result remains above and in its
original JSON. This confirms the slice's improvement; changes in PS timing between
runs are not attributed to native optimizations. No filter/reference/health/adapter/
renderer/network or public end-to-end claim. Both reviewers had completed native
runs; no competing tests/builds were intentionally started during this measurement.
