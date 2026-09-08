const ADAPTER = path self ../Nushell/modpack.nu
use $ADAPTER main

def ensure [condition: bool, message: string] {
    if not $condition { error make { msg: $message } }
}

def main [] {
    let version = (modpack --version --offline --no-human)
    ensure ((($version | describe) =~ '^record')) 'Version did not return a Nushell record.'
    ensure (($version.version? | default '') != '') 'Version record is missing its version value.'

    let failed = try {
        modpack definitely-not-a-command --no-human | ignore
        false
    } catch {
        true
    }
    ensure $failed 'A structured ModpackTools error did not become a Nushell error.'

    print 'Nushell adapter contract passed.'
}
