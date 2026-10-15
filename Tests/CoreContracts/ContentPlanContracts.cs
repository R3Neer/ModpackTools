using System.Collections.Immutable;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Providers;
using ModpackTools.Core.Resolution;
using ModpackTools.Core.Source;

internal static class ContentPlanContracts
{
    public static async Task VerifyAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ModpackTools read only plans " + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(directory, "pack"); var now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        Directory.CreateDirectory(Path.Combine(root, ".modpack"));
        try
        {
            File.WriteAllText(Path.Combine(root, ".modpack", "project.psd1"), "@{SchemaVersion=1;Id='fixture'}");
            File.WriteAllText(Path.Combine(root, "pack.toml"), "name=\"Plan\"\n[index]\nfile=\"index.toml\"\n[versions]\nminecraft=\"1.21.1\"\nfabric=\"0.16\"\n");
            File.WriteAllText(Path.Combine(root, "index.toml"), "hash-format=\"sha256\"\n");
            var project = ProjectReader.Read(root); var original = Snapshot();
            var calls = new List<string>();
            var added = await Plan(new(ContentPlanOperation.Add, ["pretty-slug", "p1"]), "add", calls);
            Assert(added.Requested.SequenceEqual(["modrinth:p1"]) && added.Changes.Length == 1 && added.Changes[0].Reason == "requested", "canonical dedup/requested change");
            Assert(added.Nodes.Single().Graph.VersionId == "new" && added.Nodes.Single().Intent == "transitive", "latest candidate with prewrite intent");
            Assert(calls.Count == 3 && added.Baseline.Issues.IsEmpty && added.Report.Issues.IsEmpty, "shared candidate metadata and complete reports");
            var repaired = await Plan(new(ContentPlanOperation.Repair, []), "repair", []);
            Assert(repaired.Nodes.IsEmpty && repaired.Changes.IsEmpty, "empty repair no-op");
            _ = await Plan(new(ContentPlanOperation.Repair, [], Type: "irrelevant"), "repair type", []);
            Directory.CreateDirectory(Path.Combine(root, "config", "fabric_loader_dependencies.json"));
            var overridden = await Plan(new(ContentPlanOperation.Repair, []), "overrides", []);
            Assert(overridden.Baseline.Unknown.Length == 1 && overridden.Report.Unknown.Length == 1, "runtime override survives baseline and solution");
            Directory.Delete(Path.Combine(root, "config", "fabric_loader_dependencies.json")); Directory.Delete(Path.Combine(root, "config"));
            Assert(Snapshot().OrderBy(pair => pair.Key).SequenceEqual(original.OrderBy(pair => pair.Key)), "planning leaves every project byte unchanged");
            var chosenConfig = Path.Combine(directory, "choices"); Directory.CreateDirectory(chosenConfig);
            File.WriteAllText(Path.Combine(chosenConfig, "last-search.json"), """{"CreatedUtc":"2026-10-15T12:00:00Z","ProjectId":null,"Results":[{"Index":1,"ProjectId":"p1"}]}""");
            var saved = new SavedProviderChoices(chosenConfig, now);
            Assert(saved.ResolveSearchNumber("1", project) == "p1", "global numeric search");
            File.WriteAllText(Path.Combine(chosenConfig, "last-search.json"), "{}");
            Assert(saved.ResolveSearchNumber("1", project) == "p1", "owned choice snapshot survives external replacement");
            Assert(new SavedProviderChoices(chosenConfig, now).ResolveSearchNumber("slug", project) is null, "literal selector bypasses malformed cache");
            File.WriteAllText(Path.Combine(chosenConfig, "last-search.json"), """{"CreatedUtc":"2026-10-15T12:00:00Z","ProjectId":null,"Results":[]}""");
            try { _ = new SavedProviderChoices(chosenConfig, now).ResolveSearchNumber(new string('9', 30), project); throw new Exception("Empty search accepted a huge number."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Search.ResultOutOfRange") { }
            File.WriteAllText(Path.Combine(chosenConfig, "last-search.json"), """{"CreatedUtc":"2026-10-15T12:00:00Z","ProjectId":null,"Results":[{"Index":{},"ProjectId":"p1"}]}""");
            try { _ = new SavedProviderChoices(chosenConfig, now).ResolveSearchNumber(new string('9', 30), project); throw new Exception("Invalid index accepted."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Search.InvalidCache") { }
            var target = new InventoryItem("modrinth:p1", ContentKind.ResourcePack, "Resources", "old.zip", "both", ContentSource.Packwiz, "old", "owned", false);
            File.WriteAllText(Path.Combine(chosenConfig, "last-versions.json"), """{"CreatedUtc":"2026-10-14T12:00:00Z","ProjectId":"FIXTURE","ItemId":"MODRINTH:P1","ItemName":"Resources","Versions":[{"Index":1,"Id":"old","VersionNumber":"1"}]}""");
            Assert(new SavedProviderChoices(chosenConfig, now).ResolveVersionChoice("1", project, target) == "old", "inclusive24h/context ignorecase");
            try { _ = new SavedProviderChoices(chosenConfig, now.AddTicks(1)).ResolveVersionChoice("1", project, target); throw new Exception("Expired choice accepted."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Versions.CacheExpired") { }
            Directory.CreateDirectory(Path.Combine(root, "resourcepacks"));
            var metadata = Path.Combine(root, "resourcepacks", "owned.pw.toml");
            File.WriteAllText(metadata, "name=\"Resources\"\nfilename=\"old.zip\"\npin=true\n[update.modrinth]\nmod-id=\"p1\"\nversion=\"old\"\n");
            var pinCalls = new List<string>();
            try { _ = await Plan(new(ContentPlanOperation.Update, ["modrinth:p1"], To: "1"), "pinned", pinCalls); throw new Exception("Pinned update accepted."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Compatibility.Pinned") { }
            Assert(pinCalls.Count == 1 && pinCalls[0].StartsWith("/v2/versions?", StringComparison.Ordinal), "pin precedes candidate and version cache IO");
            var same = await Plan(new(ContentPlanOperation.Add, ["https://modrinth.com/resourcepack/pretty-slug/version/old"]), "same", []);
            Assert(same.Changes.IsEmpty && same.Requested.Length == 1, "same pinned version remains valid explicit add request");
            File.WriteAllText(metadata, File.ReadAllText(metadata).Replace("pin=true", "pin=false", StringComparison.Ordinal));
            var exact = await Plan(new(ContentPlanOperation.Update, ["modrinth:p1"], To: "new"), "exact", []);
            Assert(exact.Changes.Single().After.Graph.VersionId == "new", "literal To bypasses missing saved version list");
            var numericConfig = Path.Combine(directory, "numeric"); Directory.CreateDirectory(numericConfig);
            File.WriteAllText(Path.Combine(numericConfig, "last-versions.json"), """{"CreatedUtc":"2026-10-15T12:00:00Z","ProjectId":"fixture","ItemId":"modrinth:p1","ItemName":"Resources","Versions":[{"Index":1,"Id":"new"}]}""");
            var numeric = await Plan(new(ContentPlanOperation.Update, ["modrinth:p1"], To: "1"), "numeric", []);
            Assert(numeric.Changes.Single().After.Graph.VersionId == "new", "numeric To consumes matching owned choice");
            var depCalls = new List<string>();
            var dependency = await Plan(new(ContentPlanOperation.Add, ["dependent"]), "dependency", depCalls);
            Assert(dependency.Nodes.Length == 3 && dependency.Changes.Any(change => change.Id == "modrinth:p2" && change.Reason == "dependency"), "solver adds declared provider dependency");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { _ = await ContentPlan.PrepareAsync(project, new(ContentPlanOperation.Repair, []), Path.Combine(directory, "cancel"), now, cancellation: cancelled.Token); throw new Exception("Plan cancellation ignored."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Core.OperationCancelled") { }
            foreach (var policy in new[] { new ContentPlanLimits(Search: new(MaximumStates: 0)), new ContentPlanLimits(Choices: new(MaximumBytes: 0)) })
            {
                try
                {
                    _ = await ContentPlan.PrepareAsync(project, new(ContentPlanOperation.Repair, []), Path.Combine(directory, "bad policy"), now, policy,
                        providerHandler: new ProviderAcquisitionContracts.Handler((_, _) => throw new Exception("Invalid policy reached HTTP.")));
                    throw new Exception("Invalid plan policy accepted.");
                }
                catch (DomainException error) when (error.Id is "ModpackTools.Compatibility.InvalidSearchPolicy" or "ModpackTools.Inventory.InvalidReferenceCacheReadPolicy") { }
            }

            Task<ContentPlan> Plan(ContentPlanRequest request, string config, List<string> endpoints) => ContentPlan.PrepareAsync(project, request, Path.Combine(directory, config), now,
                providerHandler: new ProviderAcquisitionContracts.Handler((message, _) =>
                {
                    var path = message.RequestUri!.PathAndQuery; endpoints.Add(path); string body;
                    if (path.StartsWith("/v2/versions?", StringComparison.Ordinal)) body = "[" + Version("old", "p1", "2026-01-01T00:00:00Z") + "]";
                    else if (path.Contains("/version?", StringComparison.Ordinal))
                    {
                        var id = path.Split('/')[3]; body = id == "p1" ? "[" + Version("old", id, "2026-01-01T00:00:00Z") + "," + Version("new", id, "2026-01-02T00:00:00Z") + "]"
                            : "[" + Version("latest", id, "2026-01-02T00:00:00Z", id == "p3" ? "p2" : null) + "]";
                    }
                    else { var selector = path.Split('/')[^1]; var id = selector == "dependent" ? "p3" : selector == "p2" ? "p2" : "p1"; body = "{\"id\":\"" + id + "\",\"title\":\"Resources\",\"project_type\":\"resourcepack\"}"; }
                    return Task.FromResult(ProviderAcquisitionContracts.Response(body));
                }));
            Dictionary<string, string> Snapshot() => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(root, path), path => TextHash.Compute(File.ReadAllText(path)));
        }
        finally { Directory.Delete(directory, true); }
    }
    private static string Version(string id, string project, string date, string? dependency = null) =>
        "{\"id\":\"" + id + "\",\"project_id\":\"" + project + "\",\"name\":\"Release\",\"version_number\":\"" + id + "\",\"date_published\":\"" + date
        + "\",\"files\":[{\"filename\":\"" + id + ".zip\",\"url\":\"\",\"hashes\":{}}],\"dependencies\":[" + (dependency is null ? "" : "{\"project_id\":\"" + dependency + "\",\"dependency_type\":\"required\"}") + "]}";
    private static void Assert(bool condition, string label) { if (!condition) throw new Exception("Content plan contract: " + label); }
}
