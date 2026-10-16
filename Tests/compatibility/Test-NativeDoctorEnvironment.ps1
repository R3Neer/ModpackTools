param([Parameter(Mandatory)][string]$Executable,[switch]$Scoped)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$executablePath=[IO.Path]::GetFullPath($Executable)
$temporaryBase=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$root=Join-Path $temporaryBase ('modpacktools-doctor environment-'+[guid]::NewGuid().ToString('N'))
$names=@('PATH','TEMP','TMP','APPDATA','LOCALAPPDATA','MODPACKTOOLS_CONFIG_HOME','MODPACKTOOLS_STATE_HOME','MODPACKTOOLS_PROJECT')
$previous=@{};foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
function Invoke-EnvironmentRepair([int]$TimeoutMilliseconds) {
    $tokens=@('doctor','--fix','--yes','--json','--no-human')
    if(!$Scoped){return Invoke-NativeTestProcess $executablePath $tokens -TimeoutMilliseconds $TimeoutMilliseconds}
    $request=Join-Path $root 'environment-request.json'
    [IO.File]::WriteAllText($request,(@{schema_version=1;arguments=$tokens;doctor_scope='environment';context=@{active_project='must-not-resolve-this-project'}}|ConvertTo-Json -Depth 8 -Compress))
    return Invoke-NativeTestProcess $executablePath @('--request-path',$request) -TimeoutMilliseconds $TimeoutMilliseconds
}
try {
    $configuration=Join-Path $root 'configuration spaces';$state=Join-Path $root 'stable state';$temporary=Join-Path $root 'temporary spaces'
    foreach($directory in @($configuration,$temporary,(Join-Path $root 'roaming'),(Join-Path $root 'local'))){[void][IO.Directory]::CreateDirectory($directory)}
    $configPath=Join-Path $configuration 'config.psd1';[IO.File]::WriteAllText($configPath,"@{UserSentinel='preserve me'}")
    $env:PATH='';$env:TEMP=$temporary;$env:TMP=$temporary;$env:APPDATA=Join-Path $root 'roaming';$env:LOCALAPPDATA=Join-Path $root 'local'
    $env:MODPACKTOOLS_CONFIG_HOME=$configuration;$env:MODPACKTOOLS_STATE_HOME=$state;Remove-Item Env:MODPACKTOOLS_PROJECT -ErrorAction SilentlyContinue
    $capture=Invoke-EnvironmentRepair 120000
    if($capture.Exit -ne 0 -or $capture.Stderr -ne ''){throw "Native environment repair failed: $($capture.Stdout) $($capture.Stderr)"}
    $result=($capture.Stdout|ConvertFrom-Json).data.doctor
    $repair=$result.repair
    if(-not $repair.applied -or -not $repair.packwiz_installed -or -not $repair.configuration_updated -or $repair.project_applied -or $repair.diagnostics.Count -ne 0){throw 'Environment publication receipt is inconsistent'}
    if((Get-FileHash -LiteralPath $repair.packwiz_path).Hash -cne '7182F0B513A4FF117CA252B7EAEA2CEDEEDE19DD9A0BED77A10403D75A3C9295'){throw 'Installed production executable hash mismatch'}
    if(-not [IO.File]::ReadAllText($configPath).Contains('preserve me') -or -not [IO.File]::ReadAllText($configPath).Contains('PackwizPath')){throw 'Configuration did not preserve the existing setting or publish PackwizPath'}
    if(@($result.checks|Where-Object label -eq 'PowerShell').Count -ne 0 -or (!$Scoped -and @($result.checks|Where-Object label -eq 'Invocation'|Where-Object value -eq 'Working').Count -ne 1) -or ($Scoped -and (@($result.checks).Count -ne 1 -or $result.checks[0].section -ne 'PACKWIZ'))){throw 'Native dependency diagnostic mismatch'}
    $configHash=(Get-FileHash -LiteralPath $configPath).Hash
    $capture=Invoke-EnvironmentRepair 30000
    $repeat=($capture.Stdout|ConvertFrom-Json).data.doctor.repair
    if($capture.Exit -ne 0 -or $capture.Stderr -ne '' -or $repeat.applied -or $repeat.packwiz_installed -or $repeat.configuration_updated -or (Get-FileHash -LiteralPath $configPath).Hash -cne $configHash){throw 'Repeated environment repair was not idempotent'}
    if($env:PATH -ne '' -or @(Get-ChildItem -LiteralPath $temporary -Filter '.modpacktools-bootstrap-*').Count -ne 0){throw 'Environment repair changed PATH or leaked bootstrap workspaces'}
    'Native doctor installed the production pinned Packwiz, saved config, and repeated without PowerShell or PATH changes'
} finally {
    foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name],'Process')}
    $resolved=[IO.Path]::GetFullPath($root)
    if(-not $resolved.StartsWith($temporaryBase,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-doctor environment-*'){throw 'Unsafe environment fixture cleanup'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
