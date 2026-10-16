using System.Diagnostics;
using System.Text.Json;
using ModpackTools.Core.Processes;

internal static class WindowsProcessContracts
{
    public static async Task VerifyAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "process long paths " + Guid.NewGuid().ToString("N"));
        var directory = root;
        while (directory.Length < 330) directory = Path.Combine(directory, "Unicode 雪 it's # " + new string('x', 45));
        Directory.CreateDirectory(directory);
        try
        {
            var host = Environment.ProcessPath!;
            var longHost = Path.Combine(directory, "native tool 雪.exe"); File.Copy(host, longHost);
            string[] values = ["", "two words", "quote\"here", "backslash\\", "slashes\\\\\"quote", "雪", "&|<>%!"];
            foreach (var (executable, cwd, helper) in new[] { (longHost, root, host), (host, directory, host), (longHost, directory, longHost) })
            {
                var result = await NativeTool.RunAsync(executable, ["--tool-fixture", "roundtrip", .. values], cwd, helper,
                    environment: new Dictionary<string, string?> { ["MODPACKTOOLS_PROCESS_TEST"] = "environment 雪" });
                using var json = JsonDocument.Parse(result.Stdout);
                Assert(json.RootElement.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()).SequenceEqual(values), "argument vector survives CRT quoting");
                Assert(json.RootElement.GetProperty("environment").GetString() == "environment 雪", "Unicode environment survives");
                Assert(File.ReadAllText(Path.Combine(cwd, "relative-marker.txt")) == "correct cwd", "relative writes use exact requested directory");
                Assert(result.Stderr == "fixture stderr\r\n", "stderr remains independent");
            }
            // Mixed redirect/inherit, quick exits, concurrent EOF and both long paths.
            await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
            {
                var start = new ProcessStartInfo(longHost) { UseShellExecute = false, WorkingDirectory = directory, RedirectStandardOutput = true };
                start.ArgumentList.Add("--tool-fixture"); start.ArgumentList.Add("zero-invalid");
                using var child = WindowsProcess.StartNew(start);
                var drain = child.StandardOutput.ReadToEndAsync();
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                Assert(await drain.WaitAsync(TimeSpan.FromSeconds(15)) == "" && child.ExitCode == 0, "concurrent quick-exit EOF");
            }));
            var before = Environment.CurrentDirectory;
            using (var child = WindowsProcess.StartNew(new(longHost) { UseShellExecute = false, WorkingDirectory = directory, ArgumentList = { "--tool-fixture", "zero-invalid" } }))
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert(Environment.CurrentDirectory == before, "parent current directory is unchanged");
            // Every component is already a valid 8.3 name. Even with short-name
            // generation enabled, the resulting cwd cannot fit CreateProcess.
            var impossible = root;
            for (var index = 0; index < 48; index++) impossible = Path.Combine(impossible, "abcdefgh");
            Directory.CreateDirectory(impossible);
            try
            {
                _ = await NativeTool.RunAsync(host, ["--tool-fixture", "roundtrip"], impossible, host);
                throw new Exception("Unshortenable directory was accepted");
            }
            catch (ModpackTools.Core.DomainException failure) when (failure.Id == "ModpackTools.Build.WorkingDirectoryTooLong")
            {
                Assert(failure.Details?.Contains("shorter native state", StringComparison.Ordinal) == true
                    && failure.Details.Contains(impossible, StringComparison.Ordinal), "unshortenable cwd has an actionable path-specific error");
                Assert(!File.Exists(Path.Combine(impossible, "relative-marker.txt")), "unshortenable directory never starts a child");
            }
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception("Windows process contract: " + message); }
}
