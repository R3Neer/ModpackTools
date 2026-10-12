param([string]$Repository,[string]$Executable)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $Repository 'ModpackTools.psd1') -Force
$rows=[Collections.Generic.List[object]]::new()
foreach($native in @($false,$true)) {
 $env:MODPACKTOOLS_CORE_PATH=if($native){$Executable}else{''}
 for($i=0;$i -le 21;$i++) {
  $watch=[Diagnostics.Stopwatch]::StartNew()
  $result=modpack --version --json --no-human | ConvertFrom-Json
  $watch.Stop()
  if(-not $result.ok){throw 'Bad result'}
  $rows.Add([pscustomobject]@{adapter='ps';native=$native;sample=$i;ms=$watch.Elapsed.TotalMilliseconds})
 }
}
$rows | ConvertTo-Json