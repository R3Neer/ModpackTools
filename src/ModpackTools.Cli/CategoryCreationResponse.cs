using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Cli;

internal sealed class CategoryCreationResponse(string command, string[] arguments, string root) : IDisposable
{
    private NativeOutputBuffer? buffer;
    private PublicCategories? categories;
    public ValueTask PrepareAsync(TransactionPreview<CategoryListView> preview, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); categories = PublicCategories.From(preview.Result);
        Serialize(null);
        if (buffer!.Length > 60L * 1024 * 1024) throw new DomainException("Core.OutputLimit", "Category output leaves no diagnostic reserve", "reduce the category metadata", "LimitsExceeded");
        return ValueTask.CompletedTask;
    }
    public void Write(TransactionResult<CategoryListView> result)
    {
        if (buffer is null || categories is null) throw new InvalidOperationException("Category output was not prepared.");
        if (!result.Diagnostics.IsEmpty) Serialize(ResponseDiagnostics.Bounded(result.Diagnostics, root));
        Console.OpenStandardOutput().Write(buffer.GetBuffer(), 0, checked((int)buffer.Length)); Console.WriteLine();
    }
    private void Serialize(PublicTransactionDiagnostic[]? diagnostics)
    {
        buffer?.Dispose(); buffer = new("Category creation", "reduce the category metadata before retrying");
        using var writer = new Utf8JsonWriter(buffer);
        JsonSerializer.Serialize(writer, new CategoryListEnvelope(1, true, command, arguments, new(categories!, diagnostics)), WireJsonContext.Default.CategoryListEnvelope);
    }
    public void Dispose() => buffer?.Dispose();
}
