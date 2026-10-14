using System.Globalization;
using System.Text.Json;
using Newtonsoft.Json;

namespace ModpackTools.Core.Data;

internal static class LegacyDateConstructor
{
    public static DateTime Read(JsonTextReader reader)
    {
        using var text = new StringWriter(CultureInfo.InvariantCulture);
        using var writer = new JsonTextWriter(text) { DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind };
        using var arguments = new Arguments(reader);
        writer.WriteToken(arguments, writeChildren: false);
        writer.Flush();
        using var parsed = JsonDocument.Parse(text.ToString());
        return parsed.RootElement.GetDateTime();
    }
    private sealed class Arguments : JsonReader
    {
        private readonly JsonTextReader source;
        private int count;
        public Arguments(JsonTextReader source) { this.source = source; SetToken(JsonToken.StartConstructor, "Date"); }
        public override bool Read()
        {
            if (!source.Read()) return false;
            if (source.TokenType == JsonToken.Integer && ++count > 7)
                throw new FormatException("A Date constructor accepts at most seven arguments.");
            SetToken(source.TokenType, source.Value);
            return true;
        }
    }
}
