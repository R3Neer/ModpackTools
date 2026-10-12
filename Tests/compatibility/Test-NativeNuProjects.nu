use ../../Nushell/modpack.nu

def main [] {
    let previous_path = $env.PATH
    $env.PATH = []
    let catalog = (modpack project list --no-human)
    if ($catalog | describe) !~ '^(list|table)' or ($catalog | length) != 1 or $catalog.0.id != 'fixture' or $catalog.0.name != null {
        error make {msg: 'Native Nu nullable project catalog mismatch'}
    }
    let upper = (modpack PROJECT LIST --no-human)
    if $upper.projects.items != $catalog {
        error make {msg: 'Native Nu case-sensitive unwrap changed'}
    }
    let flags = (modpack project list --ascii --color never --no-human)
    if $flags != $catalog { error make {msg: 'Native Nu presentation flags changed data'} }
    let failure = try { modpack project list --project absent --no-human; null } catch {|err| $err.msg }
    if $failure == null or $failure !~ 'ModpackTools.Option.ForbiddenCombination' {
        error make {msg: 'Native Nu project failure mismatch'}
    }
    $env.PATH = $previous_path
    print 'Native Nu project catalog and failures passed without PowerShell'
}
