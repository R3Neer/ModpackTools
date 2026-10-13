using System.Collections.Immutable;
using System.Globalization;
using ModpackTools.Core.Data;

internal static class ImportedLookupContracts
{
    public static void Verify()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "en-US", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                var table = PsdReader.Parse("@{I=1;Nested=@{I=2};Array=@(@{I=3});'N­ame'='Imported'}");
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name == "tr-TR" ? "en-US" : "tr-TR");
                foreach (var entries in new[] { table.Entries, ((PsdTable)table.Entries["Nested"]).Entries, ((PsdTable)((PsdArray)table.Entries["Array"]).Items[0]).Entries })
                {
                    if (entries.ContainsKey("ı") != (name == "tr-TR") || entries.ContainsKey("i") != (name == "en-US"))
                        throw new InvalidOperationException("Imported tables lost their captured culture.");
                }
                if (!table.Entries.ContainsKey("Name")) throw new InvalidOperationException("Imported ignored-character lookup failed.");
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Reject("@{I=1;i=2}");
                if (name == "tr-TR") Reject("@{I=1;ı=2}");
                else if (PsdReader.Parse("@{I=1;ı=2}").Entries.Count != 2) throw new InvalidOperationException("Import culture was replaced by invariant equality.");
            }
            var programmatic = new PsdTable(ImmutableDictionary<string, PsdValue>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase).Add("é", new PsdString("literal")));
            if (programmatic.Entries.ContainsKey("e\u0301")) throw new InvalidOperationException("Programmatic table comparer was rewritten.");
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    private static void Reject(string source)
    {
        try { PsdReader.Parse(source); }
        catch (PsdParseException) { return; }
        throw new InvalidOperationException("Imported duplicate key was accepted.");
    }
}
