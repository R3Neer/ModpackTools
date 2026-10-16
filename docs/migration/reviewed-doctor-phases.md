# Reviewed doctor repair phases

Design118/119, not implemented by this document. The existing public doctor
--fix machine operation remains All by default; additive internal request scopes
Environment and Project will permit the Nu adapter to ask independent consents.
No command-line aliases or new PowerShell requirement are introduced.

- Validate scope/review-token combinations before diagnostics or acquisition.
  Scope is valid only for doctor --fix without help. A project token is valid
  only for Project --yes without dry-run/help; removal token rules remain.
- Environment must not resolve/read the active project's catalog or project.
  Its diagnostic receipt is deliberately limited to environment/dependency facts.
  It never applies project changes. Project never installs tools or writes user
  configuration; a missing tool fails explicitly.
- Snapshot parsed configuration, file hash and physical directory identity.
  Freeze tool resolution within the operation. Project preview emits ordered
  visible content plan/health plus a local SHA256 observation token, binding
  config identity/hash, selected tool, strict/downgrade flags, full physical
  project/source expectation and changes. It is not authentication/signature.
- Check the token after a newly computed preview, including no-op, before apply.
  Apply under that fresh source expectation, and check the actual preparation
  callback again before publication so visible plan/health cannot silently change.
  Revalidate configuration identity/hash at these boundaries. This detects
  observed external changes; it does not promise cross-root atomicity against
  noncooperating editors. No late configuration read may change the frozen tool.
- Nu interactive flow: environment consent for fixed verified Packwiz package;
  optional root setup via existing native config set; project preview/render,
  separate project confirmation and reviewed application. Explicit --yes may
  keep the existing fresh All behavior. No new PATH write is implied by consent.
- Default-yes applies only to a real empty TTY response. EOF/null/input failure
  always cancels. Noninteractive no-yes returns preview without project writes.
  Dry-run never installs/configures/applies. Keep no-human data intact.
- Environment publication has an independent truthful receipt; project rollback
  cannot undo it. Retained evidence/uncertain ownership cuts later phases and
  reports exact paths rather than continuing automatically.

White-box design review required the pre-effect scope/token guard, strict phase
separation, coherent configuration/tool observation and independent publication
truth above. Typed contracts, AOT execution and focused failure/success tests are
required before this design is accepted as implemented.
