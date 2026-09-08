const ADAPTER = path self ../Nushell/modpack.nu
use $ADAPTER main

def ensure [condition: bool, message: string] {
    if not $condition { error make { msg: $message } }
}

def main [] {
    # Default Nu usage keeps human R3CLI output on stderr while stdout remains
    # parseable structured data. This call deliberately does not use --no-human.
    let visible_version = (modpack --version --offline)
    ensure ((($visible_version | describe) =~ '^record')) 'Visible version output contaminated the Nu pipeline.'
    ensure (($visible_version.version? | default '') != '') 'Visible version record is missing its version value.'

    let quiet_version = (modpack --version --offline --no-human)
    ensure ((($quiet_version | describe) =~ '^record')) 'No-human version did not return a Nushell record.'
    ensure (($quiet_version.version? | default '') != '') 'No-human version record is missing its version value.'

    # --wrapped must pass --help through to ModpackTools rather than allowing Nu
    # to replace the canonical command catalogue with generated wrapper help.
    let help_result = (modpack --help --no-human)
    ensure ((($help_result | describe) =~ '^record')) 'Global help did not pass through the wrapped adapter.'

    let failed = try {
        modpack definitely-not-a-command --no-human | ignore
        false
    } catch {
        true
    }
    ensure $failed 'A structured ModpackTools error did not become a Nushell error.'

    print 'Nushell adapter contract passed.'
}
