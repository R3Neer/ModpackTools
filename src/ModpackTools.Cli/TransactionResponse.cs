using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Cli;

// Own the final public bytes before pending publication; domain operations do
// not know about console output, JSON, or presentation options.
internal sealed class TransactionResponse(string command, string[] arguments, bool dryRun, string root, bool isPreview = false) : IDisposable
{
    private NativeOutputBuffer? buffer;
    private PublicTransactionChange[]? changes;
    private PublicInventoryItem[]? resources;

    public ValueTask PrepareAsync<T>(TransactionPreview<T> preview, CancellationToken token) => PrepareAsync(preview, token, null);

    public ValueTask PrepareAsync<T>(TransactionPreview<T> preview, CancellationToken token, PublicInventoryItem[]? resourcePacks)
    {
        token.ThrowIfCancellationRequested();
        resources = resourcePacks;
        changes = preview.Changes.Select(change => new PublicTransactionChange(change.Before is null ? "add" : change.After is null ? "remove" : "change", change.Path, null)).ToArray();
        Serialize(new(!dryRun && !isPreview && changes.Length != 0, dryRun, isPreview, changes.Length, changes));
        // Reserve for fifteen bounded diagnostics plus summary, including full
        // Windows paths (32767 UTF16 chars) under worst-case JSON escaping.
        if (buffer!.Length > 60L * 1024 * 1024)
            throw new DomainException("Core.OutputLimit", "Content transaction output leaves no diagnostic reserve", "reduce the requested batch", "LimitsExceeded");
        return ValueTask.CompletedTask;
    }

    public void Write<T>(TransactionResult<T> result)
    {
        if (buffer is null || changes is null) throw new InvalidOperationException("Transaction output was not prepared.");
        // Committed is terminal: no cancellation checkpoint or fallible domain
        // work may replace Applied=true with an unapplied result.
        if (!result.Diagnostics.IsEmpty)
        {
            Serialize(new(result.Applied, dryRun, isPreview, changes.Length, changes, ResponseDiagnostics.Bounded(result.Diagnostics, root)));
        }
        Console.OpenStandardOutput().Write(buffer.GetBuffer(), 0, checked((int)buffer.Length)); Console.WriteLine();
    }

    private void Serialize(PublicTransaction transaction)
    {
        buffer?.Dispose(); buffer = new("Content transaction", "reduce the requested batch before applying it");
        using var writer = new Utf8JsonWriter(buffer);
        JsonSerializer.Serialize(writer, new TransactionEnvelope(1, true, command, arguments, new(transaction, resources)), WireJsonContext.Default.TransactionEnvelope);
    }
    public void Dispose() => buffer?.Dispose();
}
