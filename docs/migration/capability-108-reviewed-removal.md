# Capability108: source-bound removal review across invocations

Native AOT142 accepts an optional request-root review_token, exactly64 hexadecimal
characters. It is valid only with content remove --yes, without dry-run or help.
Malformed tokens fail as Core.InvalidRequest before domain configuration access;
wrong command/policy fails as ModpackTools.Transaction.InvalidReview.

Human JSON removal envelopes add presentation with kind=removal, review_token,
requested identities and items containing id/name/reason. Machine --no-human
keeps its previous envelope/data without this field. This is a callable native
contract, not yet the Nu interactive confirmation UI.

The versioned SHA256 digest uses length-prefixed UTF8 fields, distinct null and
empty values, array counts, physical root identity, complete source fingerprint,
ordered Path/Before/After changes, requested IDs and all displayed rows. Hashing
uses a bounded encoding buffer. It is an observation digest, not authentication,
expiry, or a stored approval. Explicit --yes remains available without a token.

Confirmation recomputes a safe preview and compares the supplied digest before
publication; disagreement raises ModpackTools.Transaction.ReviewChanged. A match
then applies with that fresh preview's full TransactionExpectation, retaining
the existing lock, source verification, journal, rollback and failure controls.
Changes during the subsequent apply are still checked by the transaction engine.
This strengthens legacy's ExpectedChanges check with whole-source/physical-root
binding across separate processes; no source change is silently approved.

Locked restore, scoped full format verification, Release and Native AOT142 pass
with zero warnings. Protocol15, version and the full fresh-process Nu suite pass.
Parent Test-NativeRemovalReview.ps1 uses private paths with spaces, PATH empty,
real pinned Packwiz, stable repeated previews, exact application with lowercase
token, unchanged machine data, unrelated source edit rejection and request guards.
Two initial harness attempts were corrected: identical display names collided in
the generated destination, and domain error expectations omitted their existing
ModpackTools prefix. The same executable passed after harness-only corrections.

Black's two own scenarios pass faithful cascade application and selected-JAR edit
rejection, compared against frozen legacy observations. Legacy rejects changed
ordered changes; native returns the declared ReviewChanged error and preserves
the edited tree. A harness shape check initially expected lowercase hex despite
the declared64hex contract; raw native uppercase output was preserved, and the
shape assertion corrected without rerunning or altering results.
White's two complementary controls pass malformed nonhex token before invalid
configuration and token-A/selection-B rejection with source bytes intact.
White retained one initial harness-only wrong protocol error-prefix expectation.

SHA256 CLI142:
12E4F15DE5AA454AAD4A4AD1AF2293BDE94F8FAECE2AD9740904283B245507CB.
White evidence: assessment/review-whitebox/capability-108-removal-review-final.md.
Black evidence: assessment/review-blackbox, run r108n-013327, with immutable
original proposal/captures and the final acceptance report.
No SDK runtime, PowerShell runtime, live activation, performance claim, source
relocation, publication or push is introduced by this capability.
