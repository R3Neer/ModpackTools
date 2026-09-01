@{
    RootModule        = 'ModpackTools.psm1'
    ModuleVersion     = '0.5.1'
    GUID              = 'dc256dd6-6b3d-4bc5-aed0-14dad616642b'
    Author            = 'ModpackTools contributors'
    CompanyName       = 'Community'
    Copyright         = '(c) ModpackTools contributors'
    Description       = 'Lightweight CLI for managing Packwiz projects and exporting them to Modrinth.'
    PowerShellVersion = '7.0'
    FunctionsToExport = @('modpack')
    CmdletsToExport   = @()
    VariablesToExport = @()
    AliasesToExport   = @()
    PrivateData       = @{
        PSData = @{
            Tags       = @('Minecraft', 'Packwiz', 'Modrinth')
            ProjectUri = 'https://example.invalid/ModpackTools'
        }
    }
}
