# Capability120: trusted native package preparation

Product checkpoint284c170 implements the package-reader boundary of
[native-package-schema.md](native-package-schema.md). It does not install,
activate, update or execute payloads. Public version remains4.0.2.

The core authenticates the complete ZIP against an externally supplied SHA256
before parsing, bounds central directory/expansion/each file, rejects legacy
PowerShell archives, unsafe/unknown/duplicate paths and incomplete payloads.
The strict manifest requires exactly17 ordered payloads, schema/protocol1 and
win-x64; content-derived BuildId and payload lengths/hashes are checked.
Manifest JSON is canonicalized in the prepared tree. All three executables must
be AMD64 PE32+ applications without CLR headers or DLL flags. This proves file
format only: the installer must later smoke actual component protocols.

Preparation creates an exclusively reserved GUID directory below the supplied
staging parent. It reports bounded-copy progress, supports cancellation, flushes
exclusive outputs and returns physical identity plus hashes of all18 files,
including canonical package.json. Verification enumerates the complete known
tree, including directories; it does not use SourceTree's .git exclusion.
Failure cleanup first verifies every owned file/directory. Unknown/changed/
incomplete evidence is retained with exact Target and ModpackTools.RetainStage.
NTFS named streams on both files and directories prevent deletion. Ordinary
concurrent filesystem changes still require cooperative ownership; this is not
an ACL or hostile-process security boundary.

## Native acceptance

- Final CoreContracts162 SHA256:
  5C1FD141485A6648DCF3240690419DB35968E30F319BFD38C6937BFA478ED524.
- Final CLI163 SHA256:
  950D3237AC17EF64192352E5436523A4169011CB4565DED7F0AF3754FA79CFA2.
- Locked restore; whitespace/full formatting verification; Release builds and
  both win-x64 Native AOT publications pass without warnings.
- Test-NativePackage.ps1 on162:10 inspector calls (one fixture builder plus nine
  preparations): exact staged bytes/identity/progress, wrong trusted digest,
  legacy, traversal, duplicate manifest field, post-extraction payload hash
  failure with owned cleanup, foreign main-file change/cancel, file ADS/cancel
  and directory ADS/cancel. All pass; own temporary fixtures are removed safely.
- Final CLI163 protocol15 cases/version pass. Default CoreContracts162 PSD1,
  typed-value/classification/reference/alias checks pass in private configuration
  and state; this does not claim every optional inspection mode was exercised.
- Independent black-box:2 scenarios/3 calls on160, full17 payload bytes plus
  canonical manifest and wrong-digest/legacy boundaries. Subsequent directory
  ADS delta is covered by parent162 and source review, not black-box162.
- Independent white-box:2 controls/3 calls on162, coherent managed AMD64 CLR
  payload rejected and cleaned; foreign completed main-file edit/cancel retained
  with exact path. File/directory ADS protection reviewed separately in source.

Evidence outside the product checkout:
assessment/review-blackbox/native-package120-final-report.md and
assessment/review-whitebox/capability-120-native-package-final.md. The format
fixture is assessment/native-package120/format fixture.zip, SHA256
641132EC31B85CDDDE79FBD487F70125925BACF47CE825DB034E4355846046DF,
BuildId71CBDF4E39E300DD6CBCDD23302D953B67E2410E30CB934FF50D61DDF4281A3F.
Its three EXE roles are copies of CLI156 and other assets are test texts: this
is deliberately not a functioning installer/launcher/Nu distribution.

## Failed attempts and limits

Initial restricted dotnet-format failed to connect its Windows pipe; the same
authorized build ran in the normal Windows context. The first test-helper build
failed because DiagnosticTarget is typed rather than string; corrected before
any AOT artifact. The first parent harness run160 expected Package.* rather than
the existing public ModpackTools.Package.* prefix; valid preparation and hash
rejection occurred, assertion was corrected without product changes. Black-box
initial expectations about GUID prefix and17/18 receipt rows were corrected
against the same captured calls, retaining raw false checks. No product defect
is hidden by these harness corrections.

White-box found a real cleanup gap for foreign NTFS named file streams; parent
and white review extended the same fix to directory streams. Both are tested
against162. No payload execution, live installation, profile/PATH modification,
HTTP, release, push or relocation occurs in this capability. Remaining work is
version publication/control transaction, native setup/launcher/update, complete
installed workflow and performance acceptance, documentation and live handoff.
