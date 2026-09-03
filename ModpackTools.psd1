@{
    RootModule        = 'ModpackTools.psm1'
    ModuleVersion     = '3.0.3'
    GUID              = 'dc256dd6-6b3d-4bc5-aed0-14dad616642b'
    Author            = 'ModpackTools contributors'
    CompanyName       = 'Community'
    Copyright         = '(c) 2026 R3Neer'
    Description       = 'Lightweight CLI for managing Packwiz projects and exporting them to Modrinth.'
    PowerShellVersion = '7.0'
    FunctionsToExport = @('modpack')
    CmdletsToExport   = @()
    VariablesToExport = @()
    AliasesToExport   = @()
    PrivateData       = @{
        PSData = @{
            Tags       = @('Minecraft', 'Packwiz', 'Modrinth')
            ProjectUri = 'https://github.com/R3Neer/ModpackTools'
            LicenseUri = 'https://github.com/R3Neer/ModpackTools/blob/main/LICENSE'
        }
    }
}
