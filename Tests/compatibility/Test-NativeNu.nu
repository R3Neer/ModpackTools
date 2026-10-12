use ../../Nushell/modpack.nu

def main [] {
    # Remove PowerShell from process lookup: these operations must use only core.
    let previous_path = $env.PATH
    $env.PATH = []
    let version = (modpack --version --no-human)
    if $version != {version: '4.0.2'} { error make {msg: 'Native Nu version mismatch'} }
    let alias = (modpack -V --no-human)
    if $alias != $version { error make {msg: 'Native Nu alias mismatch'} }
    for extra in ['extra' 'current'] {
        let failure = try { modpack --version $extra --no-human; null } catch {|err| $err.msg }
        if $failure == null or ($failure !~ 'ModpackTools.Command.InvalidArguments') {
            error make {msg: 'Native Nu did not preserve structured domain failure'}
        }
    }
    $env.PATH = $previous_path
    print 'Native Nu version and domain failure passed without PowerShell'
}
