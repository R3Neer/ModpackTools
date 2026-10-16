use ../../Nushell/modpack.nu

def main [] {
    let previous_path = $env.PATH
    let previous_project = $env.MODPACKTOOLS_PROJECT?
    $env.PATH = []
    for active in [null '' ' ' 'stale' "á O'Brien"] {
        if $active == null { hide-env --ignore-errors MODPACKTOOLS_PROJECT } else { $env.MODPACKTOOLS_PROJECT = $active }
        for arguments in [['project' 'current'] ['PROJECT' 'CURRENT'] ['project' 'current' '--ascii' '--color' 'never']] {
            let result = (modpack ...$arguments --no-human)
            if $result != {active_project: $active} {
                error make {msg: 'Native Nu current changed the session snapshot or unwrapped value'}
            }
            if $env.MODPACKTOOLS_PROJECT? != $active { error make {msg: 'Current changed the parent environment'} }
        }
    }
    let failure = try { modpack project current extra --project forbidden --no-human; null } catch {|err| $err.msg }
    if $failure == null or $failure !~ 'ModpackTools.Option.ForbiddenCombination' { error make {msg: 'Native Nu current priority changed'} }
    $env.PATH = $previous_path
    if $previous_project == null { hide-env --ignore-errors MODPACKTOOLS_PROJECT } else { $env.MODPACKTOOLS_PROJECT = $previous_project }
    print 'Native Nu current preserves parent session without PowerShell'
}
