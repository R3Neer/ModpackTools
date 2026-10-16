using System.Collections.Immutable;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Source;
using ModpackTools.Core.Transactions;

internal static class ProjectTransactionInspection
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Use --inspect-project-transaction <request-json>.");
            using var file = File.OpenRead(args[1]); if (file.Length > 4 * 1024 * 1024) throw new ArgumentException("Transaction fixture exceeds4MiB.");
            using var document = JsonDocument.Parse(file, new() { MaxDepth = 32 }); var input = document.RootElement;
            var root = Path.GetFullPath(Text(input, "Root")); var state = Path.GetFullPath(Text(input, "StateDirectory"));
            var identity = ProjectTransaction.InspectIdentity(root); var trace = new List<TransactionEvent>();
            if (Flag(input, "Describe"))
            {
                using var output = new ProviderCacheInspection.BoundedOutput();
                using (var writer = new Utf8JsonWriter(output))
                {
                    writer.WriteStartObject(); WriteProject(writer, identity);
                    writer.WriteString("Home", Path.Combine(state, "native-transactions", "v1", identity.FileIdentity));
                    writer.WriteString("LegacyLock", Path.Combine(state, "transactions", TextHash.Compute(identity.CanonicalRoot.ToLowerInvariant()), "lock"));
                    writer.WriteString("BeforeFingerprint", SourceFingerprint.All(SourceTree.Read(root))); writer.WriteEndObject();
                }
                Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length)); return 0;
            }
            var context = input.TryGetProperty("Project", out var contextValue) ? contextValue : default;
            var project = new ProjectContext(Text(context, "Id", "fixture"), root, Text(context, "Name", "fixture"), Text(context, "Version", "1"),
                Text(context, "OutputName", "fixture.mrpack"), Text(context, "MinecraftVersion", "1.21"), Text(context, "Loader", "fabric"), Text(context, "LoaderVersion", "0.16"),
                Text(context, "IndexFile", "index.toml"), Path.Combine(root, Text(context, "IndexFile", "index.toml")));
            TransactionExpectation? expected = null;
            if (input.TryGetProperty("ExpectedChanges", out var changes)) expected = new(identity,
                Text(input, "ExpectedBeforeFingerprint", SourceFingerprint.All(SourceTree.Read(root))),
                [.. changes.EnumerateArray().Select(change => new SourceChange(Text(change, "Path"), Optional(change, "Before"), Optional(change, "After")))]);
            var tree = new SourceTreeReadLimits(); var recovery = new SourceTreeReadLimits();
            if (input.TryGetProperty("MaximumFiles", out var maximumFiles)) tree = tree with { MaximumFiles = maximumFiles.GetInt32() };
            if (input.TryGetProperty("MaximumBytes", out var maximumBytes)) tree = tree with { MaximumBytes = maximumBytes.GetInt64() };
            if (input.TryGetProperty("RecoveryMaximumFiles", out var recoveryFiles)) recovery = recovery with { MaximumFiles = recoveryFiles.GetInt32() };
            if (input.TryGetProperty("RecoveryMaximumBytes", out var recoveryBytes)) recovery = recovery with { MaximumBytes = recoveryBytes.GetInt64() };
            var result = ProjectTransaction.ExecuteAsync(project, state, Prepare, Verify, Flag(input, "DryRun"), expected,
                new(Tree: tree, RecoveryTree: recovery), observer: Observe).GetAwaiter().GetResult();
            using var capture = new ProviderCacheInspection.BoundedOutput();
            using (var writer = new Utf8JsonWriter(capture))
            {
                writer.WriteStartObject(); writer.WriteString("Result", result.Result); writer.WriteBoolean("Applied", result.Applied);
                WriteChanges(writer, "Changes", result.Changes); writer.WriteStartObject("Expectation"); WriteProject(writer, result.Expectation.Project);
                writer.WriteString("BeforeFingerprint", result.Expectation.BeforeFingerprint); WriteChanges(writer, "Changes", result.Expectation.Changes); writer.WriteEndObject();
                writer.WriteStartArray("Diagnostics"); foreach (var diagnostic in result.Diagnostics)
                { writer.WriteStartObject(); writer.WriteString("Code", diagnostic.Code); writer.WriteString("Path", diagnostic.Path); writer.WriteString("Message", diagnostic.Message); writer.WriteEndObject(); }
                writer.WriteEndArray(); writer.WriteStartArray("Trace"); foreach (var value in trace) WriteEvent(writer, value); writer.WriteEndArray(); writer.WriteEndObject();
            }
            Console.OpenStandardOutput().Write(capture.GetBuffer(), 0, checked((int)capture.Length)); return 0;

            Task<string> Prepare(ProjectContext stage, CancellationToken token)
            {
                if (input.TryGetProperty("Mutations", out var mutations))
                {
                    if (mutations.GetArrayLength() > 1024) throw new ArgumentException("Too many fixture mutations.");
                    foreach (var mutation in mutations.EnumerateArray())
                    {
                        var path = Contained(stage.Root, Text(mutation, "Path"));
                        switch (Text(mutation, "Operation"))
                        {
                            case "write": Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, Text(mutation, "Text", "")); break;
                            case "delete": File.Delete(path); break;
                            case "mkdir": Directory.CreateDirectory(path); break;
                            case "rename": var destination = Contained(stage.Root, Text(mutation, "To")); File.Move(path, destination); break;
                            default: throw new ArgumentException("Unknown fixture mutation.");
                        }
                    }
                }
                if (input.TryGetProperty("ExternalAfterPrepare", out var external)) File.WriteAllText(Contained(root, Text(external, "Path")), Text(external, "Text"));
                if (Flag(input, "FailPrepare")) throw new IOException("fixture prepare failure");
                return Task.FromResult(Text(input, "Result", "prepared"));
            }
            Task Verify(ProjectContext stage, CancellationToken token)
            { if (Flag(input, "FailVerify")) throw new IOException("fixture verify failure"); return Task.CompletedTask; }
            async ValueTask Observe(TransactionEvent value)
            {
                if (trace.Count >= 10000) throw new ArgumentException("Transaction fixture trace limit."); trace.Add(value);
                if (Optional(input, "TracePath") is { } tracePath)
                {
                    using var buffer = new MemoryStream(); using (var writer = new Utf8JsonWriter(buffer)) WriteEvent(writer, value);
                    using var log = new FileStream(tracePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                    if (buffer.Length > 64 * 1024 * 1024 - log.Length) throw new ArgumentException("Transaction trace exceeds64MiB.");
                    log.Write(buffer.GetBuffer(), 0, checked((int)buffer.Length)); log.WriteByte(10); log.Flush(true);
                }
                if (value.Phase == TransactionPhase.FileCommitted && input.TryGetProperty("ExternalAfterWrites", out var external))
                    File.WriteAllText(Contained(root, Text(external, "Path")), Text(external, "Text"));
                if (value.Phase == TransactionPhase.FileCommitted && input.TryGetProperty("CorruptBackup", out var corrupt))
                    File.WriteAllText(Contained(Path.Combine(value.Workspace, "backup"), corrupt.GetString()!), "damaged backup");
                if (value.Phase == TransactionPhase.FileCommitted && input.TryGetProperty("FailAfterWrites", out var fail) && fail.GetInt32() == value.CompletedFiles)
                    throw new IOException("fixture commit failure");
                if (value.Phase.ToString() == Optional(input, "FailPhase")) throw new IOException("fixture phase failure");
                if (value.Phase.ToString() != Optional(input, "PausePhase") || value.Phase == TransactionPhase.FileCommitted
                    && input.TryGetProperty("PauseAfterWrites", out var afterWrites) && afterWrites.GetInt32() != value.CompletedFiles) return;
                if (Flag(input, "CreatePartialPublication"))
                {
                    using var journal = JsonDocument.Parse(File.ReadAllText(Path.Combine(value.Workspace, "journal.json")));
                    var relative = journal.RootElement.GetProperty("PublicationTemporaries")[0].GetString()!;
                    var temporary = Contained(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(temporary)!);
                    File.WriteAllText(temporary, "partial publication");
                }
                File.WriteAllText(Text(input, "MarkerPath"), value.Workspace);
                await Task.Delay(Timeout.Infinite).ConfigureAwait(false);
            }
        }
        catch (DomainException error) { ManagedInspection.WriteFailure(error.Id, error.Message, error.Category, error.Target, error.Hint, error.Details); return 1; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or OverflowException or InvalidOperationException or JsonException or KeyNotFoundException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", error.Message, "InvalidData", null, "check transaction fixture input", null); return 2; }
    }
    private static string Text(JsonElement value, string field, string fallback = "") => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(field, out var item) && item.ValueKind != JsonValueKind.Null ? item.GetString()! : fallback;
    private static string? Optional(JsonElement value, string field) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(field, out var item) && item.ValueKind != JsonValueKind.Null ? item.GetString() : null;
    private static bool Flag(JsonElement value, string field) => value.TryGetProperty(field, out var item) && item.GetBoolean();
    private static string Contained(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (Path.IsPathRooted(relative) || !path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Fixture path escapes root.");
        return path;
    }
    private static void WriteProject(Utf8JsonWriter writer, ProjectPhysicalIdentity value)
    { writer.WriteStartObject("Project"); writer.WriteString("CanonicalRoot", value.CanonicalRoot); writer.WriteString("FileIdentity", value.FileIdentity); writer.WriteEndObject(); }
    private static void WriteChanges(Utf8JsonWriter writer, string name, ImmutableArray<SourceChange> changes)
    { writer.WriteStartArray(name); foreach (var change in changes) { writer.WriteStartObject(); writer.WriteString("Path", change.Path); writer.WriteString("Before", change.Before); writer.WriteString("After", change.After); writer.WriteEndObject(); } writer.WriteEndArray(); }
    private static void WriteEvent(Utf8JsonWriter writer, TransactionEvent value)
    { writer.WriteStartObject(); writer.WriteString("Phase", value.Phase.ToString()); writer.WriteString("Workspace", value.Workspace); writer.WriteString("RelativePath", value.RelativePath); writer.WriteNumber("CompletedFiles", value.CompletedFiles); writer.WriteEndObject(); }
}
