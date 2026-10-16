# Native package schema1

Design120 before implementation. Package validation/preparation is not itself
installation, update or launcher activation. Existing distribution contracts apply.

package.json identifies product ModpackTools.Native, rid win-x64, schema1, public
version, core_protocol1, bootstrap_protocol1, build_id and ordered files. Build ID
is SHA256 of the source-generated canonical identity fields plus ordered path,
length and SHA256 for every payload. It is distinct from public release version.
The manifest does not hash itself; its content is canonicalized when prepared,
so harmless JSON formatting cannot create different bytes under one build ID.
Expected archive SHA256 must be supplied by trusted local input or the verified
official release channel. An archive's own hashes are not authentication.

Exact payload paths (ordered ordinally in the manifest):

- modpacktools.exe (native core)
- modpack.exe (stable-launcher payload)
- modpacktools-setup.exe (native setup/update helper)
- cli.nu (external human CLI entry)
- Nushell/modpack.nu
- Nushell/Native-Presentation.nu
- Nushell/Native-Views.nu
- Nushell/Render-Native.nu
- presentation/r3cli/mod.nu
- presentation/r3cli/resources.json
- presentation/r3cli/LICENSE
- presentation/r3cli/hashes.json
- presentation/r3cli/manifest.json
- LICENSE
- licenses/Tomlyn.txt
- licenses/Newtonsoft.Json.txt
- README.md

No DLL/JIT/SDK/PowerShell source or arbitrary executable is a runtime payload.
Recognizable legacy PowerShell ZIPs fail explicitly. Optional ZIP directory entries
are limited to ancestors of those exact paths, with no duplicates/case collisions,
backslashes, reparse/symlink metadata or unknown files. Validate all paths before
extracting. Require exact declared lengths and hashes. Each exe must be PE32+
AMD64 executable without CLR header. That proves native format, not application
identity; installation must still smoke its actual protocols before switching.

Default bounds: archive256MiB, expanded512MiB, <=64 ZIP entries, manifest64KiB,
exe128MiB each, Nu2MiB each, JSON8MiB each, other text/license1MiB each. Manifest
objects reject missing/unknown/duplicate fields, invalid types and out-of-order
or duplicate entries. No unbounded extraction/recursion or unsafe shell commands.
Progress reports phase, exact path, completed/total bytes and files; cancellation
is checked during hashing/copying. Output files use exclusive creation and flush.

Preparation reserves an owned GUID directory under a caller-owned staging parent,
keeps the parent/root physical identities and verifies the complete known set.
On failure cleanup removes only verified owned contents; unknown/changed/partial
uncertain evidence is retained with exact path and ModpackTools.RetainStage.
Never recursively erase an unverified foreign tree. Prepared data is not activated.

Human external launcher may use the user's existing native Nushell for R3CLI;
--no-human native execution must remain possible without PowerShell or .NET SDK.
The installed Nu bootstrap statically pins module/core/resources to one version.
No global Nu plugins/profiles are changed without explicit target selection.
The first native release may remain unpublished under the user's restriction;
local trusted-package install/update/rollback must still be fully executable.
Legacy public release assets are never a fallback for native self-update.
