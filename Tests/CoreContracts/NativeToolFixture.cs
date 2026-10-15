using System.Diagnostics;

internal static class NativeToolFixture
{
    public static int Init(string[] args)
    {
        string Value(string flag) { var index = Array.IndexOf(args, flag); return index < 0 || index + 1 == args.Length ? "" : args[index + 1]; }
        var name = Value("--name"); var loader = Value("--modloader"); var version = Value("--" + loader + "-version");
        if (version.Length == 0) version = "fixture-latest";
        var evidence = Environment.GetEnvironmentVariable("MODPACKTOOLS_TEST_INIT_ARGUMENTS_PATH");
        if (!string.IsNullOrEmpty(evidence))
        {
            using var stream = File.Create(evidence); using var writer = new System.Text.Json.Utf8JsonWriter(stream);
            writer.WriteStartArray(); foreach (var argument in args) writer.WriteStringValue(argument); writer.WriteEndArray();
        }
        var utf8 = new System.Text.UTF8Encoding(false);
        File.WriteAllText("pack.toml", $"name=\"{name}\"\nversion=\"{Value("--version")}\"\n[index]\nfile=\"index.toml\"\n[versions]\nminecraft=\"{Value("--mc-version")}\"\n{loader}=\"{version}\"\n", utf8);
        File.WriteAllText("index.toml", "hash-format=\"sha256\"\nfiles=[]\n", utf8);
        if (name == "Fail Pack") return Run(["both-fail"]);
        return 0;
    }
    public static int Refresh() => File.Exists("fixture-refresh-fail.marker") ? Run(["both-fail"]) : Run(["zero-invalid"]);
    public static int Run(string[] args)
    {
        var mode = args.FirstOrDefault() ?? "zero-invalid";
        switch (mode)
        {
            case "both-fail": Console.WriteLine("fixture stdout"); Console.Error.WriteLine("fixture stderr"); return 7;
            case "both-ok": Console.WriteLine("fixture stdout"); Console.Error.WriteLine("fixture stderr"); return 0;
            case "flood":
                for (var index = 0; index < 32; index++) { Console.Out.Write(new string('o', 8192)); Console.Error.Write(new string('e', 8192)); }
                return 0;
            case "writer":
                File.WriteAllText("fixture-writer.pid", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                while (true) { File.AppendAllText("fixture-writer.log", "owned\n"); Thread.Sleep(20); }
            case "spawn-and-exit":
            case "spawn-and-hang":
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Environment.CurrentDirectory };
                start.ArgumentList.Add("--tool-fixture"); start.ArgumentList.Add("writer");
                using (var child = Process.Start(start) ?? throw new IOException("Fixture writer failed to start."))
                {
                    var wait = Stopwatch.StartNew();
                    while (!File.Exists("fixture-writer.pid") && wait.ElapsedMilliseconds < 5000) Thread.Sleep(10);
                    if (!File.Exists("fixture-writer.pid")) return 8;
                    File.WriteAllText("fixture-parent.pid", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (mode == "spawn-and-exit") return 0;
                    Thread.Sleep(Timeout.Infinite); return 0;
                }
            case "hang": File.WriteAllText("fixture-parent.pid", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)); Thread.Sleep(Timeout.Infinite); return 0;
            case "zero-invalid": return 0;
            default: return 9;
        }
    }
}
