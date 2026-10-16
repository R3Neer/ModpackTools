using System.Collections.Immutable;
using System.Text;
using ModpackTools.Core.Data;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Processes;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Resolution;

public sealed record ContentPinningRequest(ImmutableArray<string> Selectors, bool Pinned, bool DryRun = false);
public sealed record ContentPinningLimits(TransactionLimits? Transaction = null, NativeToolLimits? Tool = null,
    int MaximumDocumentCharacters = 4 * 1024 * 1024);

public static class ContentPinning
{
    public static async Task<TransactionResult<ImmutableArray<string>>> ExecuteAsync(ProjectContext project, ContentPinningRequest request,
        string configurationDirectory, string stateDirectory, DateTimeOffset now, Func<PackwizObservation> resolvePackwiz, string nativeHost,
        ContentPinningLimits? limits = null, TransactionExpectation? expected = null, CancellationToken cancellation = default,
        Func<TransactionPreview<ImmutableArray<string>>, CancellationToken, ValueTask>? prepareOutput = null)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(resolvePackwiz); limits ??= new();
        if (request.Selectors.IsDefaultOrEmpty || request.Selectors.Length > 1024
            || request.Selectors.Any(value => string.IsNullOrEmpty(value) || value.Length > 4096))
            throw new DomainException("Content.InvalidPinRequest", "Pinning requires one through 1024 bounded nonempty selectors", "use installed content selectors");
        if (limits.MaximumDocumentCharacters is <= 0 or > 4 * 1024 * 1024)
            throw new DomainException("Content.InvalidPinPolicy", "Pin document quota is invalid", "use a positive quota at most4Mi characters");
        var selectors = BatchSelectorResolver.ResolveFromDirectory(request.Selectors, configurationDirectory, project.Id, now,
            new(["mod", "resourcepack", "shaderpack"], requirePackwiz: true));
        return await ProjectTransaction.ExecuteAsync(project, stateDirectory, Prepare, dryRun: request.DryRun,
            expected: expected, limits: limits.Transaction, cancellation: cancellation, prepareOutput: prepareOutput).ConfigureAwait(false);

        async Task<ImmutableArray<string>> Prepare(ProjectContext stage, CancellationToken token)
        {
            var targets = UpdateTargetSelector.Resolve(InventoryReader.Read(stage), selectors);
            foreach (var target in targets)
            {
                token.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(stage.Root, target.MetadataPath!);
                var original = TransactionFiles.Within(project.Root, relative);
                var path = TransactionFiles.Within(stage.Root, relative);
                try
                {
                    var output = TomlStringEdits.ApplyLiterals(TextFile.Read(path),
                        [new("", "pin", request.Pinned ? "true" : "false")], limits.MaximumDocumentCharacters);
                    Verify(output, original);
                    File.WriteAllText(path, output, new UTF8Encoding(false, true));
                    Verify(TextFile.Read(path), original);
                }
                catch (FormatException error)
                { throw new DomainException("Content.UnsupportedPinEdit", "Packwiz pin cannot be edited safely", "normalize the root pin field before retrying", "InvalidData", original, details: error.Message); }
            }
            _ = await PackwizRefresh.RefreshAsync(stage, resolvePackwiz(), nativeHost, limits.Tool, token).ConfigureAwait(false);
            return [.. targets.Select(target => target.Id)];
        }
        void Verify(string text, string original)
        {
            var document = new TomlDocument(text, semanticFields: true);
            if (document.SemanticFields!.Get("pin") is not TomlBoolean actual || actual.Value != request.Pinned || document.RootPin() != request.Pinned)
                throw new DomainException("Content.UnsupportedPinEdit", "Packwiz pin would disagree with the displayed pin state",
                    "normalize the root pin field before retrying", "InvalidData", original);
        }
    }
}
