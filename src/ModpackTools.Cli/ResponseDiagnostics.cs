using System.Collections.Immutable;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Cli;

internal static class ResponseDiagnostics
{
    public static PublicTransactionDiagnostic[] Bounded(ImmutableArray<TransactionDiagnostic> source, string root)
    {
        var diagnostics = source.Take(15).Select(diagnostic => new PublicTransactionDiagnostic(
            diagnostic.Code[..Math.Min(64, diagnostic.Code.Length)], diagnostic.Path,
            diagnostic.Message[..Math.Min(2048, diagnostic.Message.Length)])).ToList();
        if (source.Length > 15) diagnostics.Add(new("Transaction.AdditionalDiagnostics", root, $"{source.Length - 15} additional transaction diagnostics; inspect native recovery state"));
        return [.. diagnostics];
    }
}
