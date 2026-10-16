using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ModpackTools.Core;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Providers;
using ModpackTools.Core.Source;

internal static class ArtifactProviderContracts
{
    public static async Task VerifyAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ModpackTools artifacts " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var local = Path.Combine(directory, "local mod.jar");
        byte[] payload;
        using (var memory = new MemoryStream())
        {
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            using (var writer = new StreamWriter(archive.CreateEntry("fabric.mod.json").Open(), new UTF8Encoding(false)))
                writer.Write("{\"schemaVersion\":1,\"id\":\"native_test\",\"version\":\"1.0.0\",\"environment\":\"*\"}");
            payload = memory.ToArray();
        }
        File.WriteAllBytes(local, payload);
        try
        {
            foreach (var name in new[] { "sha1", "sha256", "sha512", "md5" })
            {
                using var digest = IncrementalHash.CreateHash(new HashAlgorithmName(name.ToUpperInvariant()));
                digest.AppendData(payload);
                await using var provider = new ArtifactProvider(directory);
                var result = await provider.ReadAsync(new("", Convert.ToHexString(digest.GetHashAndReset()), name, local), "fabric");
                Assert(result.Source == VerifiedArtifactSource.Local && result.Declarations!.Mods[0].Declaration.Id == "native_test", "local digest and declarations " + name);
                Assert(provider.Statistics.NetworkBytes == 0 && provider.Statistics.VerificationBytes == payload.Length, "separate statistics");
            }
            using (var borrowed = new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                borrowed.Position = borrowed.Length;
                Assert(ArtifactArchiveReader.Read(borrowed, local, "fabric").Mods.Length == 1 && borrowed.CanRead, "borrowed reset and leave open");
            }
            var hash = Convert.ToHexString(SHA256.HashData(payload));
            var path = Path.Combine(directory, "artifacts", TextHash.Compute("sha256:" + hash));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "old", new UTF8Encoding(false));
            var old = File.ReadAllBytes(path);
            await using (var provider = new ArtifactProvider(directory,
                new(MaximumTotalBytes: old.Length + payload.Length * 2L - 1), handler: new Handler(payload)))
            {
                try { await provider.ReadAsync(new("http://127.0.0.1/artifact", hash, "sha256", Check: true), "fabric"); throw new InvalidOperationException("Expected budget failure."); }
                catch (DomainException error) { Assert(error.Id == "ModpackTools.Content.ArtifactLimit", "reserve final verification before replacement"); }
            }
            Assert(old.SequenceEqual(File.ReadAllBytes(path)), "budget failure keeps original cache");
            await using (var provider = new ArtifactProvider(directory, handler: new Handler([1, 2, 3])))
            {
                try { await provider.ReadAsync(new("http://127.0.0.1/artifact", hash, "sha256", Check: true), "fabric"); throw new InvalidOperationException("Expected mismatch."); }
                catch (DomainException error) { Assert(error.Id == "ModpackTools.Content.HashMismatch", "declared hash mismatch domain"); }
            }
            Assert(old.SequenceEqual(File.ReadAllBytes(path)), "digest failure keeps original cache");
            await using (var provider = new ArtifactProvider(directory, handler: new Handler(payload)))
            {
                var request = new ArtifactRequest("http://127.0.0.1/artifact", hash, "sha256", Check: true);
                var results = await Task.WhenAll(provider.ReadAsync(request, "fabric"), provider.ReadAsync(request, "fabric"));
                Assert(results.Count(result => result.Source == VerifiedArtifactSource.Download) == 1 && provider.Statistics.Requests == 1, "same identity serializes and reuses verified cache");
                Assert(results.All(result => result.Declarations!.Mods.Length == 1), "download and cache same-handle metadata");
            }
            Assert(!Directory.EnumerateFiles(Path.Combine(directory, "artifacts"), "*.tmp").Any(), "owned temporary cleanup");
            Assert(payload.SequenceEqual(File.ReadAllBytes(local)), "local bytes preserved");
        }
        finally { Directory.Delete(directory, recursive: true); }
        static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException("Artifact provider: " + message); }
    }
    private sealed class Handler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
    }
}
