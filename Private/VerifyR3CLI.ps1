# Shared by module import and installer staging. Deliberately independent of rendering.
function Test-MpR3Package {
    param([Parameter(Mandatory)][string]$ModuleRoot)
    $dependency = (Import-PowerShellDataFile -LiteralPath (Join-Path $ModuleRoot 'dependencies.psd1')).R3CLI
    $root = Join-Path $ModuleRoot 'Private/vendor/R3CLI'
    foreach ($file in $dependency.Files.Keys) {
        if ([IO.Path]::GetFileName($file) -cne $file) { Throw-MpError -Message 'Invalid R3CLI package filename' -Hint 'reinstall ModpackTools' -ErrorId 'Dependency.InvalidManifest' -Category InvalidData }
        $path = Join-Path $root $file
        if (-not [IO.File]::Exists($path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $dependency.Files[$file]) {
            Throw-MpError -Message "R3CLI package verification failed for '$file'" -Hint 'reinstall ModpackTools' -ErrorId 'Dependency.HashMismatch' -Category InvalidData
        }
    }
    $manifestPath = Join-Path $root 'R3CLI.psd1'
    $manifest = Import-PowerShellDataFile -LiteralPath $manifestPath
    if ($manifest.ModuleVersion -ne $dependency.Version -or $manifest.PrivateData.ApiVersion -ne 1) { Throw-MpError -Message 'Incompatible R3CLI package API' -Hint 'reinstall ModpackTools' -ErrorId 'Dependency.IncompatibleAPI' -Category InvalidData }
    [pscustomobject]@{ Path=$manifestPath; Version=$dependency.Version; Revision=$dependency.Revision }
}
