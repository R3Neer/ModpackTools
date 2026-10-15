using System.Collections.Immutable;
using System.Text;
using ModpackTools.Core.Data;
using ModpackTools.Core.Graph;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Resolution;

public sealed record ContentMaterializationLimits(int MaximumChanges = 100000,
    int MaximumDocumentCharacters = 4 * 1024 * 1024, long MaximumOutputBytes = 64 * 1024 * 1024);
public sealed record MaterializedContentFile(string Id, string MetadataPath, string VersionId);
public sealed record ContentMaterializationReceipt(ImmutableArray<MaterializedContentFile> Files, bool EditorialWritten);

public static class ContentMaterialization
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    // The caller owns this disposable stage. Publication and rollback belong to
    // ProjectTransaction, not this document operation.
    public static ContentMaterializationReceipt Apply(ProjectContext observedProject, ProjectContext stage,
        ContentPlan plan, ContentMaterializationLimits? limits = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(observedProject); ArgumentNullException.ThrowIfNull(stage); ArgumentNullException.ThrowIfNull(plan);
        limits ??= new();
        if (limits.MaximumChanges <= 0 || limits.MaximumDocumentCharacters is <= 0 or > 4 * 1024 * 1024 || limits.MaximumOutputBytes <= 0)
            throw new DomainException("Content.InvalidMaterializationPolicy", "Content document quotas are invalid", "use positive bounded quotas");
        if (plan.Changes.IsDefault || plan.Nodes.IsDefault || plan.Requested.IsDefault || !Enum.IsDefined(plan.Operation)
            || plan.Changes.Length > limits.MaximumChanges || plan.Nodes.Length > limits.MaximumChanges || plan.Requested.Length > limits.MaximumChanges)
            throw Error("InvalidPlan", "Content plan is incomplete or exceeds its quota");
        try
        {
            cancellation.ThrowIfCancellationRequested(); WindowsProjectIdentity.GuardAncestors(stage.Root);
            var metadata = EditorialMetadataReader.Read(stage); var data = metadata.Data.Entries;
            var content = metadata.Content?.Entries ?? ImmutableDictionary<string, PsdValue>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
            var outputs = new List<(string Path, string Text)>(); var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var outputParents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var receipts = ImmutableArray.CreateBuilder<MaterializedContentFile>(); long bytes = 0;
            var nodes = new Dictionary<string, ProjectContentNode>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in plan.Nodes) if (!nodes.TryAdd(node.Graph.Id, node)) throw Error("InvalidPlan", "Content plan repeats a node", node.Graph.Id);
            foreach (var change in plan.Changes)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!ids.Add(change.Id) || !nodes.TryGetValue(change.Id, out var node) || node != change.After
                    || !change.Id.StartsWith("modrinth:", StringComparison.Ordinal) || !StringComparer.Ordinal.Equals(node.Item.Id, change.Id))
                    throw Error("InvalidPlan", "Content change does not match its frozen node", change.Id);
                if (node.File is not { } file) continue;
                var existing = change.Before?.Item.MetadataPath;
                var relative = existing is null ? Folder(node.Item.Kind) + "/" + change.Id.Replace(':', '-') + ".pw.toml"
                    : Path.GetRelativePath(observedProject.Root, existing).Replace('\\', '/');
                if (existing is not null) _ = TransactionFiles.Within(observedProject.Root, relative);
                var destination = TransactionFiles.Within(stage.Root, relative);
                if (!paths.Add(destination)) throw Error("PathCollision", "Planned metadata destinations collide", destination);
                if (existing is null && (File.Exists(destination) || Directory.Exists(destination))) throw Error("PathCollision", "Planned metadata collides with an existing path", destination);
                _ = TransactionFiles.Within(Path.GetDirectoryName(destination)!, file.Filename);
                var text = existing is null ? "" : TextFile.Read(destination);
                var side = node.Item.Side.ToLowerInvariant() is "client" or "server" or "both" ? node.Item.Side : "both";
                var algorithm = file.Sha512.Length != 0 ? "sha512" : "sha1"; var hash = algorithm == "sha512" ? file.Sha512 : file.Sha1;
                var edits = new TomlStringEdit[] { new("", "name", node.Item.Name), new("", "filename", file.Filename), new("", "side", side),
                    new("download", "url", file.Url), new("download", "hash-format", algorithm), new("download", "hash", hash),
                    new("update.modrinth", "mod-id", change.Id[9..]), new("update.modrinth", "version", node.Graph.VersionId) };
                text = TomlStringEdits.Apply(text, edits, limits.MaximumDocumentCharacters);
                Verify(text, edits, destination); AddOutput(destination, text); receipts.Add(new(change.Id, relative, node.Graph.VersionId));
                if (!content.ContainsKey(change.Id)) content = content.Add(change.Id, Intent(node.Intent));
            }
            if (plan.Operation == ContentPlanOperation.Add)
                foreach (var id in plan.Requested)
                {
                    if (!nodes.ContainsKey(id)) throw Error("InvalidPlan", "Requested content is absent from the frozen nodes", id);
                    content = content.SetItem(id, Intent("explicit"));
                }
            var editorialWritten = !plan.Changes.IsEmpty || plan.Operation == ContentPlanOperation.Add && !plan.Requested.IsEmpty;
            if (editorialWritten)
            {
                data = data.SetItem("ContentSchemaVersion", new PsdNumber("1")).SetItem("Content", new PsdTable(content));
                var path = TransactionFiles.Within(stage.Root, ".modpack/metadata.psd1");
                if (!paths.Add(path)) throw Error("PathCollision", "Content metadata collides with editorial metadata", path);
                AddOutput(path, PsdDataWriter.Write(new(data), limits.MaximumDocumentCharacters));
            }
            // All destinations, prospective documents and quotas are checked before
            // the first stage write. An IO failure is handled by the outer stage owner.
            foreach (var output in outputs)
            {
                cancellation.ThrowIfCancellationRequested(); WindowsProjectIdentity.GuardAncestors(output.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(output.Path)!); File.WriteAllText(output.Path, output.Text, Utf8);
            }
            foreach (var receipt in receipts)
            {
                var text = TextFile.Read(TransactionFiles.Within(stage.Root, receipt.MetadataPath));
                var document = new TomlDocument(text, semanticFields: true);
                if (document.String("mod-id", "update.modrinth") != receipt.Id[9..] || document.String("version", "update.modrinth") != receipt.VersionId)
                    throw Error("PlanMismatch", "Materialized content differs from its selected identity/version", receipt.MetadataPath, "InvalidResult");
            }
            if (editorialWritten) _ = EditorialMetadataReader.Read(stage);
            return new(receipts.ToImmutable(), editorialWritten);

            void AddOutput(string path, string text)
            {
                if (outputParents.Contains(path)) throw Error("PathCollision", "A content document is also a planned directory", path);
                if (Directory.Exists(path)) throw Error("PathCollision", "Content document path is occupied by a directory", path);
                for (var parent = Path.GetDirectoryName(path); parent is not null && TransactionFiles.IsBelow(parent, stage.Root); parent = Path.GetDirectoryName(parent))
                {
                    if (File.Exists(parent)) throw Error("PathCollision", "Content document parent is occupied by a file", parent);
                    if (paths.Contains(parent)) throw Error("PathCollision", "A planned document occupies another document's parent", parent);
                    outputParents.Add(parent);
                }
                if (text.Length > limits.MaximumDocumentCharacters) throw Error("MaterializationLimit", "Content document exceeds its quota", path, "LimitsExceeded");
                var length = Utf8.GetByteCount(text);
                if (length > limits.MaximumOutputBytes - bytes) throw Error("MaterializationLimit", "Content documents exceed their combined byte quota", path, "LimitsExceeded");
                bytes += length; outputs.Add((path, text));
            }
        }
        catch (OperationCanceledException) { throw new DomainException("Core.OperationCancelled", "Content materialization was cancelled", "retry the enclosing transaction when ready", "OperationStopped"); }
        catch (EncoderFallbackException error) { throw Error("UnsupportedMetadataEdit", "Content metadata contains invalid Unicode", category: "InvalidData", details: error.Message); }
        catch (FormatException error) { throw Error("UnsupportedMetadataEdit", "Content metadata cannot be edited safely", details: error.Message, category: "InvalidData"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw Error("MaterializationIoFailed", "Content stage IO failed", stage.Root, "WriteError", error.Message); }
    }

    private static void Verify(string text, TomlStringEdit[] edits, string path)
    {
        var document = new TomlDocument(text, semanticFields: true);
        foreach (var edit in edits)
        {
            TomlDeclarationValue value = document.SemanticFields!;
            foreach (var section in edit.Section.Split('.', StringSplitOptions.RemoveEmptyEntries)) value = value is TomlFields table ? table.Get(section) : TomlMissing.Value;
            value = value is TomlFields fields ? fields.Get(edit.Key) : TomlMissing.Value;
            if (value is not TomlText actual || actual.Value != edit.Value) throw Error("PlanMismatch", "Prospective content declaration differs from the plan", path, "InvalidResult");
        }
        if (document.String("mod-id", "update.modrinth") != edits[6].Value || document.String("version", "update.modrinth") != edits[7].Value)
            throw Error("UnsupportedMetadataEdit", "Metadata shape is unsupported by the content reader", path, "InvalidData");
    }
    private static PsdTable Intent(string intent) => new(ImmutableDictionary<string, PsdValue>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase).Add("Intent", new PsdString(intent)));
    private static string Folder(ContentKind kind) => kind switch
    { ContentKind.Mod => "mods", ContentKind.ResourcePack => "resourcepacks", ContentKind.ShaderPack => "shaderpacks", _ => throw Error("InvalidPlan", "Content kind is invalid") };
    private static DomainException Error(string id, string message, string? target = null, string category = "InvalidOperation", string? details = null) =>
        new("Content." + id, message, "inspect the frozen plan and stage metadata", category, target, details: details);
}
