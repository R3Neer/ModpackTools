const PACKAGE_DIR = path self .
use ./Nushell/modpack.nu

def --env --wrapped main [...args: string] {
    $env.MODPACKTOOLS_CORE_PATH = ($PACKAGE_DIR | path join 'modpacktools.exe')
    modpack ...$args
}
