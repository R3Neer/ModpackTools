const SCRIPT_DIR = path self .
const POWERSHELL_INSTALLER = ($SCRIPT_DIR | path join 'Install-ModpackTools.ps1')

def external-path [name: string] {
    let matches = (which $name | where type == external)
    if ($matches | is-empty) { null } else { $matches | get path | first }
}

def main [
    --force
    --non-interactive
    --skip-doctor
    --install-path: string
    --expected-manifest-hash: string
] {
    let pwsh = (external-path 'pwsh')
    let legacy = if $pwsh == null { external-path 'powershell.exe' } else { null }
    let engine = if $pwsh != null { $pwsh } else { $legacy }

    if $engine == null {
        error make {
            msg: 'Neither PowerShell 7 nor Windows PowerShell is available. Install PowerShell or add it to PATH before running the installer.'
        }
    }

    mut forwarded = [
        '-NoLogo'
        '-NoProfile'
        '-ExecutionPolicy'
        'Bypass'
        '-File'
        $POWERSHELL_INSTALLER
    ]

    if $force { $forwarded = ($forwarded | append '-Force') }
    if $non_interactive { $forwarded = ($forwarded | append '-NonInteractive') }
    if $skip_doctor { $forwarded = ($forwarded | append '-SkipDoctor') }
    if $install_path != null {
        $forwarded = ($forwarded | append '-InstallPath' | append $install_path)
    }
    if $expected_manifest_hash != null {
        $forwarded = ($forwarded | append '-ExpectedManifestHash' | append $expected_manifest_hash)
    }

    run-external $engine ...$forwarded
    let exit_code = $env.LAST_EXIT_CODE
    if $exit_code != 0 {
        error make { msg: $'ModpackTools installation failed with exit code ($exit_code).' }
    }

    print 'Nushell installation completed. Open a new Nushell session to load the modpack command.'
}
