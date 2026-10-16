param([Parameter(Mandatory)][string]$Setup,[Parameter(Mandatory)][string]$Launcher,[Parameter(Mandatory)][string]$NativeExecutable,[Parameter(Mandatory)][string]$NuExecutable)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$source=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$setupPath=[IO.Path]::GetFullPath($Setup);$launcherPath=[IO.Path]::GetFullPath($Launcher);$corePath=[IO.Path]::GetFullPath($NativeExecutable);$nuPath=[IO.Path]::GetFullPath($NuExecutable)
$parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath());$run=Join-Path $parent ('modpacktools-nu installer-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($run)
$names=@('PATH','APPDATA','LOCALAPPDATA','MODPACKTOOLS_CONFIG_HOME','MODPACKTOOLS_STATE_HOME','MODPACKTOOLS_CORE_PATH','TEMP','TMP');$previous=@{}
foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
try {
    foreach($name in @('Roaming','Local','configuration','state','temporary')){[void][IO.Directory]::CreateDirectory((Join-Path $run $name))}
    $env:APPDATA=Join-Path $run 'Roaming';$env:LOCALAPPDATA=Join-Path $run 'Local';$env:MODPACKTOOLS_CONFIG_HOME=Join-Path $run 'configuration';$env:MODPACKTOOLS_STATE_HOME=Join-Path $run 'state';$env:MODPACKTOOLS_CORE_PATH=$null
    $env:TEMP=Join-Path $run 'temporary';$env:TMP=$env:TEMP;$env:PATH=''
    $zip=Join-Path $run 'native package.zip'
    $packed=Invoke-NativeTestProcess $setupPath @('pack','--core',$corePath,'--launcher',$launcherPath,'--assets',$source,'--output',$zip,'--json') -TimeoutMilliseconds 120000
    if($packed.Exit -ne 0 -or $packed.Stderr){throw "Native pack failed: $($packed.Stdout) $($packed.Stderr)"}
    $package=$packed.Stdout|ConvertFrom-Json
    $extracted=Join-Path $run 'extracted';Expand-Archive -LiteralPath $zip -DestinationPath $extracted
    $readme=Get-Content -LiteralPath (Join-Path $extracted 'README.md') -Raw
    if($readme -notmatch 'PowerShell and the .NET runtime are\s+not required' -or $readme -match 'PowerShell 7 is the canonical|Install-ModpackTools.ps1|PowerShell bridge'){throw 'Shipped README still prescribes legacy installation'}
    if(@(Get-ChildItem -LiteralPath $extracted -File -Recurse -Filter '*.ps1').Count){throw 'PowerShell script entered native payload'}
    $root=Join-Path $run 'native app with spaces'
    $installed=Invoke-NativeTestProcess $nuPath @('--no-config-file',(Join-Path $source 'install-modpack-tools.nu'),'--root',$root,'--package',$zip,'--sha256',$package.sha256,'--setup',(Join-Path $extracted 'modpacktools-setup.exe'),'--state',$env:MODPACKTOOLS_STATE_HOME,'--json') -TimeoutMilliseconds 240000
    if($installed.Exit -ne 0 -or $installed.Stderr){throw "Nu native install failed: $($installed.Stdout) $($installed.Stderr)"}
    $receipt=$installed.Stdout|ConvertFrom-Json
    if(-not $receipt.root_created -or -not $receipt.control_switched -or $receipt.root -cne $root){throw 'Native install receipt mismatch'}
    $version=Invoke-NativeTestProcess (Join-Path $root 'bin/modpack.exe') @('--version','--no-human') -TimeoutMilliseconds 30000
    if($version.Exit -ne 0 -or $version.Stderr -or ($version.Stdout|ConvertFrom-Json).data.version.version -cne '4.0.2'){throw 'Installed native launcher failed'}
    if(@(Get-ChildItem -LiteralPath $env:APPDATA -Recurse -Force).Count -or @(Get-ChildItem -LiteralPath $env:MODPACKTOOLS_CONFIG_HOME -Recurse -Force).Count){throw 'Installer changed profiles or configuration'}
    if(@(Get-ChildItem -LiteralPath $env:TEMP -Recurse -Force).Count){throw 'Installer left temporary evidence unexpectedly'}
    'Nu installer, shipped native README, installed launcher and no-profile/no-PowerShell payload checks passed with empty PATH'
} finally {
    foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name])}
    $resolved=[IO.Path]::GetFullPath($run)
    if(-not $resolved.StartsWith($parent.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolved -Leaf) -notmatch '^modpacktools-nu installer-[0-9a-f]{32}$'){throw 'Unsafe fixture cleanup path'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
