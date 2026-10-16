using System.Globalization;
using ModpackTools.Core;
using ModpackTools.Core.Inventory;

internal static class DefaultOptionsContracts
{
    public static void Verify()
    {
        var local = new InventoryItem("local", ContentKind.Mod, "Default Options", "local.jar", "unknown", ContentSource.Local, null, null, null);
        var managed = local with { Id = "MODRINTH:weg59Z5B", Name = "Renamed", Source = ContentSource.Packwiz, Filename = null };
        if (!ReferenceEquals(DefaultOptionsDetector.Find([local, managed]), local) || !ReferenceEquals(DefaultOptionsDetector.Find([managed, local]), managed))
            throw new InvalidOperationException("Default Options changed first-match ownership or required source/JAR fields.");
        var absentConfig = new DefaultOptionsState(local, false, "missing-config");
        if (!ReferenceEquals(DefaultOptionsDetector.AssertInstalled(absentConfig, "fixture"), absentConfig) || absentConfig.Ready)
            throw new InvalidOperationException("Default Options assertion began requiring config readiness.");
        VerifyMissing();
        foreach (var filename in new[] { "defaultoptions-1.jar", "defaultoptions_.jar", "defaultoptions..jar", "defaultoptions-.jar", "defaultoptions-1.jar\n" })
            if (DefaultOptionsDetector.Find([local with { Name = "Other", Filename = filename }]) is null)
                throw new InvalidOperationException("Default Options filename stopped matching: " + filename);
        foreach (var filename in new[] { null, "", "defaultoptions.jar", "prefix-defaultoptions-1.jar", "defaultoptions-1.jar\r\n", "defaultoptions-1.jar\n\n", "defaultoptions-\n1.jar" })
            if (DefaultOptionsDetector.Find([local with { Name = "Other", Filename = filename }]) is not null)
                throw new InvalidOperationException("Default Options filename unexpectedly matched: " + filename);
        if (DefaultOptionsDetector.Find([]) is not null) throw new InvalidOperationException("Empty mod inventory yielded Default Options.");
        VerifyDefaultArray();
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "tr-TR", "en-US", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                var ascii = DefaultOptionsDetector.Find([local with { Name = "Other", Filename = "DEFAULTOPTIONS-1.JAR" }]);
                if ((ascii is not null) != (culture == "en-US")) throw new InvalidOperationException("Default Options matcher retained a previous invocation's culture.");
            }
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    private static void VerifyMissing()
    {
        try { DefaultOptionsDetector.AssertInstalled(new(null, true, "config"), "fixture"); }
        catch (DomainException exception) when (exception.Id == "ModpackTools.ResourcePack.DefaultOptionsRequired" && exception.Category == "NotInstalled" && exception.Target is TextTarget { Value: "fixture" }) { return; }
        throw new InvalidOperationException("Default Options prerequisite error changed.");
    }

    private static void VerifyDefaultArray()
    {
        try { DefaultOptionsDetector.Find(default); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Default mod array was accepted.");
    }
}
