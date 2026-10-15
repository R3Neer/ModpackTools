using System.Collections.Immutable;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Processes;
using ModpackTools.Core.Resolution;
using ModpackTools.Core.Source;

internal static class ContentOperationContracts
{
    public static async Task VerifyAsync(string packwiz)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mpt-op-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var root = Path.Combine(directory, "pack"); Directory.CreateDirectory(Path.Combine(root, ".modpack"));
            File.WriteAllText(Path.Combine(root, ".modpack", "project.psd1"), "@{SchemaVersion=1;Id='fixture'}");
            File.WriteAllText(Path.Combine(root, ".modpack", "metadata.psd1"), "@{Categories=@{quality=@{Name='Quality'}};Mods=@{};ResourcePacks=@{}}");
            File.WriteAllText(Path.Combine(root, ".packwizignore"), ".modpack/**\nfixture-refresh-fail.marker\n");
            File.WriteAllText(Path.Combine(root, "pack.toml"), "name=\"Fixture\"\nversion=\"1\"\npack-format=\"packwiz:1.1.0\"\n[index]\nfile=\"custom.index.toml\"\nhash-format=\"sha256\"\nhash=\"stale\"\n[versions]\nminecraft=\"1.21.1\"\nfabric=\"0.16\"\n");
            File.WriteAllText(Path.Combine(root, "custom.index.toml"), "hash-format=\"sha256\"\nfiles=[]\n");
            var project = ProjectReader.Read(root); var config = Path.Combine(directory, "config"); var state = Path.Combine(directory, "state");
            var now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero); var calls = new List<string>();
            var request = new ContentOperationRequest(new(ContentPlanOperation.Add, ["resource"]));
            var before = SourceFingerprint.All(SourceTree.Read(root));
            var preview = await Execute(request with { DryRun = true });
            Assert(!preview.Applied && preview.Changes.Length >= 3 && SourceFingerprint.All(SourceTree.Read(root)) == before, "complete dry-run preserves protected root");
            var applied = await Execute(request, preview.Expectation);
            Assert(applied.Applied && File.Exists(Path.Combine(root, "resourcepacks", "modrinth-r1.pw.toml")), "complete add publishes declarations and refreshed index");
            Assert(applied.Result.Changes.Length == 1 && applied.Result.Requested.SequenceEqual(["modrinth:r1"]), "owned selected plan result");
            var stable = SourceFingerprint.All(SourceTree.Read(root));
            var noVersionChange = await Execute(request);
            Assert(noVersionChange.Result.Changes.IsEmpty && !noVersionChange.Applied && SourceFingerprint.All(SourceTree.Read(root)) == stable, "same-version explicit add retains already explicit data");
            try { _ = await Execute(request with { Category = "quality" }); throw new Exception("Resource category accepted."); }
            catch (DomainException error) when (error.Id == "ModpackTools.Option.CategoryRequiresMod") { }
            Assert(SourceFingerprint.All(SourceTree.Read(root)) == stable, "requested type error before protected writes");
            try
            {
                _ = await ContentOperation.ExecuteAsync(project, new(new(ContentPlanOperation.Repair, [])), config, state, now,
                    () => new(true, packwiz, "fixture", null), Environment.ProcessPath!, providerHandler: Handler,
                    prepareOutput: (_, _) => throw new DomainException("Core.OutputLimitExceeded", "fixture output budget", "bounded output", "LimitsExceeded"));
                throw new Exception("Output failure accepted.");
            }
            catch (DomainException error) when (error.Id == "ModpackTools.Core.OutputLimitExceeded") { }
            Assert(SourceFingerprint.All(SourceTree.Read(root)) == stable, "output preparation failure occurs before publication");
            File.WriteAllText(Path.Combine(root, "fixture-refresh-fail.marker"), "owned fault"); stable = SourceFingerprint.All(SourceTree.Read(root));
            try
            {
                _ = await ContentOperation.ExecuteAsync(project, new(new(ContentPlanOperation.Repair, [])), config, state, now,
                    () => new(true, Environment.ProcessPath!, "fixture", null), Environment.ProcessPath!, providerHandler: Handler);
                throw new Exception("Refresh failure accepted.");
            }
            catch (DomainException error) when (error.Id == "ModpackTools.Build.ProcessFailed") { }
            Assert(SourceFingerprint.All(SourceTree.Read(root)) == stable, "real native subprocess failure leaves protected root intact");
            async Task<ModpackTools.Core.Transactions.TransactionResult<ContentPlan>> Execute(ContentOperationRequest value,
                ModpackTools.Core.Transactions.TransactionExpectation? expected = null)
            {
                var start = calls.Count;
                var result = await ContentOperation.ExecuteAsync(project, value, config, state, now,
                    () => new(true, packwiz, "fixture", null), Environment.ProcessPath!, expected: expected, providerHandler: Handler);
                Assert(calls.Skip(start).Count(path => path.Contains("/project/resource", StringComparison.Ordinal)) <= 1, "one owned identity lookup per operation");
                return result;
            }
            HttpMessageHandler Handler() => new ProviderAcquisitionContracts.Handler((message, _) =>
            {
                var path = message.RequestUri!.PathAndQuery; calls.Add(path);
                var version = """{"id":"v1","project_id":"r1","name":"Release","version_number":"1","date_published":"2026-01-01T00:00:00Z","files":[{"filename":"resource.zip","url":"https://example.invalid/resource.zip","hashes":{"sha1":"0000000000000000000000000000000000000000"}}],"dependencies":[],"loaders":["minecraft"],"game_versions":["1.21.1"]}""";
                var body = path.StartsWith("/v2/versions?", StringComparison.Ordinal) || path.Contains("/version?", StringComparison.Ordinal) ? "[" + version + "]"
                    : path.Contains("/version/", StringComparison.Ordinal) ? version : """{"id":"r1","title":"Resource","project_type":"resourcepack"}""";
                return Task.FromResult(ProviderAcquisitionContracts.Response(body));
            });
        }
        finally { Directory.Delete(directory, true); }
    }
    private static void Assert(bool value, string label) { if (!value) throw new Exception("Content operation contract: " + label); }
}
