using System.Collections.Immutable;
using ModpackTools.Core.Source;

namespace ModpackTools.Core.Transactions;

public sealed record TransactionLimits(SourceTreeReadLimits? Tree = null, int MaximumWorkspaces = 100,
    long MaximumJournalBytes = 64 * 1024 * 1024, int MaximumChanges = 100000, int MaximumCleanupEntries = 1000000,
    SourceTreeReadLimits? RecoveryTree = null);
public enum TransactionPhase { LeaseAcquired, RecoveryCompleted, StageFileCopied, StageCopied, Prepared, Verified, BackupFileCopied, BackedUp, JournalWritten, FileCommitted, StateVerified, Committed }
public sealed record TransactionEvent(TransactionPhase Phase, string Workspace, string? RelativePath = null, int CompletedFiles = 0);
public sealed record TransactionDiagnostic(string Code, string Path, string Message);
public sealed record TransactionExpectation(ProjectPhysicalIdentity Project, string BeforeFingerprint, ImmutableArray<SourceChange> Changes);
public sealed record TransactionPreview<T>(T Result, ImmutableArray<SourceChange> Changes, TransactionExpectation Expectation);
// T is the callback's owned result. It must not rely on temporary stage paths
// remaining available after ExecuteAsync returns.
public sealed record TransactionResult<T>(T Result, ImmutableArray<SourceChange> Changes, bool Applied,
    TransactionExpectation Expectation, ImmutableArray<TransactionDiagnostic> Diagnostics);

internal sealed record TransactionOwner(string TransactionId, ProjectPhysicalIdentity Project, string Status);
internal sealed record TransactionJournal(TransactionOwner Owner, string BeforeFingerprint,
    ImmutableArray<SourceChange> Changes, ImmutableArray<string> CreatedDirectories, ImmutableArray<string> PublicationTemporaries, string Status);
