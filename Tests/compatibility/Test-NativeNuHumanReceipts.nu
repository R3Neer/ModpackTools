use ../../Nushell/modpack.nu

def human [tokens: list<string>] {
    if (modpack ...$tokens --ascii --color never) != null {error make {msg: 'Human transaction receipt leaked a pipeline value'}}
}

def source-bytes [root: string] {
    ['pack.toml' 'index.toml' '.modpack/metadata.psd1' 'mods/side_fixture.jar' 'resourcepacks/receipt.pw.toml'] | each {|relative|
        {path: $relative, hash: (open --raw ($root | path join $relative) | hash sha256)}
    }
}

def main [packwiz: string] {
    let previous_path = $env.PATH
    let previous_project = $env.MODPACKTOOLS_PROJECT?
    $env.PATH = []
    $env.MODPACKTOOLS_PROJECT = 'nu-created'
    let pwd = $env.PWD
    modpack config set packwiz $packwiz --no-human | ignore
    let root = (modpack project status --no-human).project.root
    mkdir ($root | path join resourcepacks)
    let managed = ($root | path join 'resourcepacks/receipt.pw.toml')
    'name = "Managed Receipt"
filename = "receipt.zip"
side = "client"
pin = false
[download]
url = "https://example.invalid/receipt.zip"
hash-format = "sha256"
hash = "0000000000000000000000000000000000000000000000000000000000000000"
' | save $managed
    modpack category create ui-receipts --name 'UI Receipts' --no-human | ignore
    for tokens in [['category' 'assign' 'ui-receipts' 'side_fixture.jar'] ['mod' 'set-side' 'client' 'side_fixture.jar'] ['content' 'pin' 'receipt.zip']] {
        let before = (source-bytes $root)
        human ($tokens | append '--dry-run')
        if (source-bytes $root) != $before {error make {msg: 'Human transaction dry-run changed source'}}
        human $tokens
    }
    let rows = (modpack content list --no-human)
    let local = ($rows | where filename == 'side_fixture.jar' | first)
    if $local.category != 'ui-receipts' or $local.side != 'client' or not ($rows | where filename == 'receipt.zip' | first | get pinned) {error make {msg: 'Human transaction did not apply category/side/pin state'}}
    # No-op receipt and the inverse operations use the same public transaction.
    human ['mod' 'set-side' 'client' 'side_fixture.jar']
    for tokens in [['category' 'clear' 'side_fixture.jar'] ['category' 'remove' 'ui-receipts'] ['content' 'unpin' 'receipt.zip']] {
        let before = (source-bytes $root)
        human ($tokens | append '--dry-run')
        if (source-bytes $root) != $before {error make {msg: 'Inverse human transaction dry-run changed source'}}
        human $tokens
    }
    let rows = (modpack content list --no-human)
    if ($rows | where filename == 'side_fixture.jar' | first | get category) != 'unclassified' or ($rows | where filename == 'receipt.zip' | first | get pinned) or (modpack category list --no-human | any {|row| $row.id == 'ui-receipts'}) {error make {msg: 'Human inverse transaction state mismatch'}}
    rm $managed
    human ['mod' 'set-side' 'host' 'side_fixture.jar']
    modpack config set packwiz auto --no-human | ignore
    if $env.MODPACKTOOLS_PROJECT != 'nu-created' or $env.PATH != [] or $env.PWD != $pwd {error make {msg: 'Human transactions changed parent selection/PATH/PWD'}}
    $env.PATH = $previous_path
    if $previous_project == null {hide-env --ignore-errors MODPACKTOOLS_PROJECT} else {$env.MODPACKTOOLS_PROJECT = $previous_project}
    print 'Native human category/side/pin receipts, dry-runs and inverse operations passed without PowerShell'
}
