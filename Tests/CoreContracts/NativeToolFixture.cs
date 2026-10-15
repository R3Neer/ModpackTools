using System.Diagnostics;

internal static class NativeToolFixture
{
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
