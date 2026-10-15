using System.Net;
using System.Text;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Graph;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Providers;
using ModpackTools.Core.Resolution;
using ModpackTools.Core.Source;

internal static class ResolutionAcquisitionContracts
{
    public static async Task VerifyAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ModpackTools candidate acquisition " + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(directory, "pack"); var config = Path.Combine(directory, "config");
        Directory.CreateDirectory(Path.Combine(root, ".modpack"));
        try
        {
            File.WriteAllText(Path.Combine(root, ".modpack", "project.psd1"), "@{SchemaVersion=1;Id='fixture'}");
            File.WriteAllText(Path.Combine(root, "pack.toml"), "name=\"Candidates\"\n[index]\nfile=\"index.toml\"\n[versions]\nminecraft=\"1.21.1\"\nfabric=\"0.16\"\n");
            File.WriteAllText(Path.Combine(root, "index.toml"), "hash-format=\"sha256\"\n");
            var project = ProjectReader.Read(root); var now = DateTimeOffset.UtcNow; var handler = new Reply();
            await using (var session = await ResolutionAcquisition.CreateAsync(project, config, now, providerHandler: handler))
            {
                if (session.InitialObservation?.Nodes.Length != 0) throw new Exception("Empty initial observation changed.");
                var domain = await session.GetCandidatesAsync("modrinth:p1", default);
                if (handler.Calls != 2 || domain.VersionIds.Length != 2) throw new Exception("Candidate domain transport changed.");
                var node = await session.GetNodeAsync("modrinth:p1", "v1", default);
                if (handler.Calls != 2 || node.Graph.Kind != "resourcepack" || node.Graph.Side != "client" || node.Explicit) throw new Exception("Resource candidate defaults or lazy acquisition changed.");
                _ = await session.GetNodeAsync("modrinth:p1", "v1", default);
                using var stored = JsonDocument.Parse(File.ReadAllText(Path.Combine(config, "metadata", TextHash.Compute("version/v1") + ".json")));
                if (stored.RootElement.GetProperty("extra").GetProperty("owned").GetInt32() != 42) throw new Exception("Selected candidate cache lost owned extra fields.");
                if (File.Exists(Path.Combine(config, "metadata", TextHash.Compute("version/v2") + ".json"))) throw new Exception("Unselected version was persisted.");
            }
            var limited = Path.Combine(directory, "limited");
            await using (var session = await ResolutionAcquisition.FromObservedNodesAsync(project, [], limited, now,
                new(MaximumNodes: 1), providerHandler: new Reply()))
            {
                _ = await session.GetNodeAsync("modrinth:p1", "v1", default);
                try { _ = await session.GetNodeAsync("modrinth:p1", "v2", default); throw new Exception("Candidate node quota missed."); }
                catch (DomainException error) when (error.Id == "ModpackTools.Compatibility.AcquisitionLimit") { }
            }
            var item = new InventoryItem("modrinth:p1", ContentKind.Mod, "Installed", "old.jar", "both", ContentSource.Packwiz, "old", null, false);
            var before = new ProjectContentNode(item, new(item.Id, "old", item.Name, "mod", "both", true, [], [], ["prior warning"]), null, null, false, "explicit");
            var warnings = Path.Combine(directory, "warnings");
            await using (var session = await ResolutionAcquisition.FromObservedNodesAsync(project, [before], warnings, now,
                new(new(Graph: new(MaximumWarnings: 1))), providerHandler: new Reply()))
            {
                try { _ = await session.GetNodeAsync("modrinth:p1", "v1", default); throw new Exception("Shared declaration ledger reset."); }
                catch (DomainException error) when (error.Id == "ModpackTools.Compatibility.GraphLimitExceeded" && error.Target is TextTarget { Value: "MaximumWarnings" }) { }
                if (File.Exists(Path.Combine(warnings, "metadata", TextHash.Compute("version/v1") + ".json"))) throw new Exception("Failed candidate was persisted.");
            }
            await using (var session = await ResolutionAcquisition.FromObservedNodesAsync(project, [], Path.Combine(directory, "wire"), now,
                new(new(Provider: new(MaximumListingWireBytes: 1))), providerHandler: new Reply()))
            {
                try { _ = await session.GetCandidatesAsync("modrinth:p1", default); throw new Exception("Owned candidate wire quota missed."); }
                catch (DomainException error) when (error.Id == "ModpackTools.Metadata.ProviderRequestLimit") { }
            }
            await using (var client = new ModrinthVersionClient(Path.Combine(directory, "standalone"), now, new(MaximumListingWireBytes: 1), handler: new Reply()))
            {
                var observed = await ProviderCandidates.ObserveAsync(Path.Combine(directory, "standalone"), "p1", "1.21.1", "fabric", now, client);
                if (observed.Versions.Length != 2) throw new Exception("Standalone candidate observations inherited planning wire retention.");
            }
            var deep = new string('[', 65) + "0" + new string(']', 65);
            var deepBody = Reply.Rows.Replace("\"extra\":{\"owned\":42}", "\"extra\":" + deep, StringComparison.Ordinal);
            var deepConfig = Path.Combine(directory, "deep");
            await using (var session = await ResolutionAcquisition.FromObservedNodesAsync(project, [], deepConfig, now,
                new(new(Provider: new(Decode: new(MaximumDepth: 100)))), providerHandler: new Reply(deepBody)))
            {
                _ = await session.GetNodeAsync("modrinth:p1", "v1", default);
                using var stored = JsonDocument.Parse(File.ReadAllText(Path.Combine(deepConfig, "metadata", TextHash.Compute("version/v1") + ".json")), new() { MaxDepth = 100 });
                if (stored.RootElement.GetProperty("extra").ValueKind != JsonValueKind.Array) throw new Exception("Validated deep owned wire changed.");
            }
            var leaf = new ManifestRequirement("modrinth:p9", VersionConstraint.Text("*"), "required", "both", "project", ModpackTools.Core.Versions.VersionScheme.General, "provider", "", [], [], null);
            var nested = leaf with { Target = "", Any = [leaf] };
            var nestedBefore = before with { Graph = before.Graph with { Requirements = [nested], Warnings = [] } };
            var dependencyBody = Reply.Rows.Replace("\"dependencies\":[]", "\"dependencies\":[{\"project_id\":\"p2\",\"dependency_type\":\"required\"}]", StringComparison.Ordinal);
            await using (var session = await ResolutionAcquisition.FromObservedNodesAsync(project, [nestedBefore], Path.Combine(directory, "nested"), now,
                new(new(Graph: new(MaximumRequirements: 2))), providerHandler: new Reply(dependencyBody)))
            {
                try { _ = await session.GetNodeAsync("modrinth:p1", "v1", default); throw new Exception("Nested before requirement ledger reset."); }
                catch (DomainException error) when (error.Id == "ModpackTools.Compatibility.GraphLimitExceeded" && error.Target is TextTarget { Value: "MaximumRequirements" }) { }
            }
            try
            {
                await using var session = await ResolutionAcquisition.FromObservedNodesAsync(project, [before with { Item = item with { Id = "other" } }], Path.Combine(directory, "mismatch"), now, providerHandler: new Reply());
                throw new Exception("Inconsistent observed identity was admitted.");
            }
            catch (DomainException error) when (error.Id == "ModpackTools.Compatibility.CandidateIdentityMismatch") { }
            var dated = before with { Version = new("old", "p1", "Old", "1", new("2026/01/01 00:00:00", null, null), [], [], [], []) };
            if (ResolutionAcquisition.ToResolutionNode(dated).OrderingDate?.Ticks != new DateTime(2026, 1, 1).Ticks) throw new Exception("Publication text fallback was lost.");
            var budgetRoot = Path.Combine(directory, "domain budget pack");
            Directory.CreateDirectory(Path.Combine(budgetRoot, ".modpack")); Directory.CreateDirectory(Path.Combine(budgetRoot, "mods"));
            File.Copy(Path.Combine(root, ".modpack", "project.psd1"), Path.Combine(budgetRoot, ".modpack", "project.psd1"));
            File.Copy(Path.Combine(root, "pack.toml"), Path.Combine(budgetRoot, "pack.toml"));
            File.Copy(Path.Combine(root, "index.toml"), Path.Combine(budgetRoot, "index.toml"));
            File.WriteAllText(Path.Combine(budgetRoot, "mods", "item.pw.toml"), "name=\"Budget\"\nfilename=\"missing.jar\"\n[update.modrinth]\nmod-id=\"p1\"\nversion=\"v1\"\n");
            var domainBudget = new DomainBudgetReply();
            try
            {
                await using var session = await ResolutionAcquisition.CreateAsync(ProjectReader.Read(budgetRoot), Path.Combine(directory, "domain budget cache"), now,
                    new(MaximumDomains: 1), providerHandler: domainBudget);
                throw new Exception("Initial health acquired too many candidate domains.");
            }
            catch (DomainException error) when (error.Id == "ModpackTools.Compatibility.AcquisitionLimit" && error.Target is TextTarget { Value: "MaximumDomains" }) { }
            if (domainBudget.ProjectLookups != 1) throw new Exception("Initial health domain quota was checked after acquisition.");
        }
        finally { Directory.Delete(directory, true); }
    }
    private sealed class Reply(string? customRows = null) : HttpMessageHandler
    {
        public const string Rows = """[{"id":"v2","project_id":"p1","name":"Second","version_number":"2","date_published":"2026-01-02T00:00:00Z","files":[{"filename":"second.zip","url":"","hashes":{}}],"dependencies":[],"loaders":["minecraft"],"game_versions":["1.21.1"]},{"id":"v1","project_id":"p1","name":"First","version_number":"1","date_published":"2026-01-01T00:00:00Z","files":[{"filename":"first.zip","url":"","hashes":{}}],"dependencies":[],"loaders":["minecraft"],"game_versions":["1.21.1"],"extra":{"owned":42}}]""";
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var path = request.RequestUri!.AbsolutePath;
            var body = path.EndsWith("/project/p1", StringComparison.Ordinal) ? """{"id":"p1","title":"Resources","project_type":"resourcepack","server_side":"unsupported","client_side":"required"}"""
                : customRows ?? Rows;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
    private sealed class DomainBudgetReply : HttpMessageHandler
    {
        public int ProjectLookups { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath; string body;
            if (path.EndsWith("/versions", StringComparison.Ordinal)) body = """[{"id":"v1","project_id":"p1","files":[],"dependencies":[{"project_id":"p2","dependency_type":"required"},{"project_id":"p3","dependency_type":"required"}]}]""";
            else if (path.EndsWith("/version", StringComparison.Ordinal)) body = "[]";
            else { ProjectLookups++; var id = path.Split('/')[^1]; body = "{\"id\":\"" + id + "\",\"project_type\":\"resourcepack\"}"; }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
