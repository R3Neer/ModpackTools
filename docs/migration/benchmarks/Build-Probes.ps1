param(
    [Parameter(Mandatory)][string]$OutputRoot,
    [string]$LegacyRoot = ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))),
    [Parameter(Mandatory)][string]$RustcPath,
    [Parameter(Mandatory)][string]$CargoPath,
    [Parameter(Mandatory)][string]$RustupHome,
    [Parameter(Mandatory)][string]$CargoHome,
    [Parameter(Mandatory)][string]$RustLinker,
    [string]$NuGetPackages
)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($OutputRoot)
if(Test-Path -LiteralPath $root){throw 'Choose a new output directory; do not overwrite prior evaluation output'}
[void][IO.Directory]::CreateDirectory($root)
foreach($name in @('Measure-Startup.ps1','Measure-Hash.ps1','Measure-Legacy.ps1','startup-go.go','dotnet-startup','rust-probe')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $root $name) -Recurse
}
$adapter=(Join-Path ([IO.Path]::GetFullPath($LegacyRoot)) 'Nushell/modpack.nu').Replace('\','/')
if($adapter.Contains("'")){throw 'Diagnostic Nu constant generator requires a path without apostrophes; choose an alternate evaluation checkout'}
$nuSource=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Measure-NuSession.nu'))
$nuSource=$nuSource.Replace('const ADAPTER = path self ../work/ModpackTools/Nushell/modpack.nu',"const ADAPTER = '$adapter'")
[IO.File]::WriteAllText((Join-Path $root 'Measure-NuSession.nu'),$nuSource,[Text.UTF8Encoding]::new($false))
$names=@('DOTNET_CLI_HOME','DOTNET_CLI_TELEMETRY_OPTOUT','DOTNET_GENERATE_ASPNET_CERTIFICATE','NUGET_PACKAGES','GOCACHE','RUSTUP_HOME','CARGO_HOME','CARGO_TARGET_X86_64_PC_WINDOWS_GNU_LINKER','RUSTFLAGS')
$previous=@{};foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
try {
    $env:DOTNET_CLI_HOME=Join-Path $root 'dotnet-home';$env:DOTNET_CLI_TELEMETRY_OPTOUT='1';$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
    $env:NUGET_PACKAGES=if($NuGetPackages){[IO.Path]::GetFullPath($NuGetPackages)}else{Join-Path $root 'nuget-packages'};$env:GOCACHE=Join-Path $root 'go-cache'
    $env:RUSTUP_HOME=$RustupHome;$env:CARGO_HOME=$CargoHome;$env:CARGO_TARGET_X86_64_PC_WINDOWS_GNU_LINKER=$RustLinker;$env:RUSTFLAGS='-C link-self-contained=yes'
    & $RustcPath --version
    if($LASTEXITCODE -ne 0){throw 'Rust compiler check failed'}
    & dotnet publish (Join-Path $root 'dotnet-startup/Startup.csproj') -c Release -r win-x64 -p:PublishAot=true -p:NuGetAudit=false -p:TrimmerSingleWarn=false -p:ILLinkTreatWarningsAsErrors=true -p:IlcTreatWarningsAsErrors=true "-p:LegacyRoot=$LegacyRoot" --nologo -o (Join-Path $root 'dotnet-aot')
    if($LASTEXITCODE -ne 0){throw 'Native AOT publication failed'}
    & $CargoPath build --offline --locked --release --manifest-path (Join-Path $root 'rust-probe/Cargo.toml')
    if($LASTEXITCODE -ne 0){throw 'Rust publication failed'}
    & go build -trimpath -o (Join-Path $root 'startup-go.exe') (Join-Path $root 'startup-go.go')
    if($LASTEXITCODE -ne 0){throw 'Go publication failed'}
    foreach($exe in @('dotnet-aot/Startup.exe','rust-probe/target/release/diagnostic-probe.exe','startup-go.exe')) {
        & (Join-Path $root $exe)
        if($LASTEXITCODE -ne 0){throw "Probe failed: $exe"}
    }
}finally{foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name],'Process')}}
