using ModpackTools.Core.Data;
using System.Globalization;
using System.Text.Json;

// Test-only transport peer: arbitrary replies exercise adapter validation before
// any parent session mutation. This mode is never part of the shipping CLI.
if (args is ["--request-path", _] && Environment.GetEnvironmentVariable("MODPACKTOOLS_TEST_RESPONSE_PATH") is { } responsePath)
{
    Console.Write(File.ReadAllText(responsePath));
    return int.Parse(Environment.GetEnvironmentVariable("MODPACKTOOLS_TEST_RESPONSE_EXIT") ?? "0", CultureInfo.InvariantCulture);
}

if (args is ["--inspect-psd1", var inputPath])
{
    try
    {
        var parsed = PsdReader.Parse(File.ReadAllText(inputPath));
        using var writer = new Utf8JsonWriter(Console.OpenStandardOutput());
        WriteData(writer, parsed);
        writer.Flush();
        return 0;
    }
    catch (PsdParseException exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 1;
    }
    catch (Exception exception) when (exception is OverflowException or FormatException or IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine("Inspector limitation or input access failure: " + exception.Message);
        return 2;
    }
}

static void WriteData(Utf8JsonWriter writer, PsdValue value)
{
    switch (value)
    {
        case PsdTable table:
            writer.WriteStartObject();
            foreach (var pair in table.Entries)
            {
                writer.WritePropertyName(pair.Key);
                WriteData(writer, pair.Value);
            }
            writer.WriteEndObject();
            break;
        case PsdArray array:
            writer.WriteStartArray();
            foreach (var item in array.Items) WriteData(writer, item);
            writer.WriteEndArray();
            break;
        case PsdString text: writer.WriteStringValue(text.Value); break;
        case PsdBoolean flag: writer.WriteBooleanValue(flag.Value); break;
        case PsdNull: writer.WriteNullValue(); break;
        case PsdNumber number:
            writer.WriteNumberValue(decimal.Parse(number.Literal, NumberStyles.Float, CultureInfo.InvariantCulture));
            break;
        default: throw new InvalidOperationException("Unknown data node");
    }
}

var table = PsdReader.Parse("# data\n@{ Root = 'C:\\O''Brien'; Flags = @($true, $false, $null)\n Number = -1.25e2; Title = \"hello`nworld\"; Nested = @{Id='á'} }");
if (table.Entries["ROOT"] is not PsdString { Value: "C:\\O'Brien" } ||
    table.Entries["Flags"] is not PsdArray { Items.Length: 3 } ||
    table.Entries["Number"] is not PsdNumber { Literal: "-1.25e2" } ||
    table.Entries["Title"] is not PsdString { Value: "hello\nworld" })
    throw new InvalidOperationException("Typed PSD1 data mismatch");

string[] invalid = ["@{Root=$(Get-Content secret)}", "@{Root=\"$env:HOME\"}", "@{A=1;a=2}", "@{A='unterminated}", "@{A=1 B=2}", "@{A=[int]1}", "@{A=Get-Date}", "@{A=@(1 2)}", "@{}; Get-Date", "<# unclosed"];
foreach (var source in invalid)
{
    try { PsdReader.Parse(source); }
    catch (PsdParseException) { continue; }
    throw new InvalidOperationException("Unsafe/invalid data accepted");
}
var arrays = PsdReader.Parse("@{A=@(1,@(2,3));B=@(@(1,2),@(3,4));C=@(@(1,2));D=\"`u{41}`u{1F600}\"}");
if (arrays.Entries["A"] is not PsdArray a || a.Items.Length != 2 || a.Items[1] is not PsdArray { Items.Length: 2 } ||
    arrays.Entries["B"] is not PsdArray b || b.Items.Length != 2 || b.Items.Any(item => item is not PsdArray { Items.Length: 2 }) ||
    arrays.Entries["C"] is not PsdArray c || c.Items.Length != 2 || c.Items[0] is not PsdNumber { Literal: "1" } ||
    arrays.Entries["D"] is not PsdString { Value: "A😀" })
    throw new InvalidOperationException("PSD1 nested arrays or Unicode escape mismatch");
var comments = PsdReader.Parse("@{A=@(1 <#x#>, @(2,3));B=1 <#x#>, 2}");
if (comments.Entries["A"] is not PsdArray { Items.Length: 2 } commented || commented.Items[1] is not PsdArray { Items.Length: 2 } ||
    comments.Entries["B"] is not PsdArray { Items.Length: 2 })
    throw new InvalidOperationException("Comments broke comma expression semantics");
foreach (var source in new[] { "@{A=@(1,)}", "@{A=@(1 <#x#>,)}", "@{A=1\n,2}", "@{<#outer <#nested#> end#>A=1}", "@{A=\"`u{D800}\"}", "@{A=١}", "@{A=\"`u{}\"}" })
{
    try { PsdReader.Parse(source); }
    catch (PsdParseException) { continue; }
    throw new InvalidOperationException("Invalid literal accepted");
}
foreach (var path in args)
{
    var data = PsdReader.Parse(File.ReadAllText(path));
    if (data.Entries.Count == 0) throw new InvalidOperationException("Real data fixture unexpectedly empty");
}
string[] limits = ["@{1='one'}", "@{A=" + string.Concat(Enumerable.Repeat("@(", 70)) + "1" + new string(')', 70) + "}", new string(' ', 4 * 1024 * 1024 + 1), "@{A=@(" + string.Join(',', Enumerable.Repeat("1", 100001)) + ")}"];
foreach (var source in limits)
{
    try { PsdReader.Parse(source); }
    catch (PsdParseException) { continue; }
    throw new InvalidOperationException("Unsupported key or parser limit not enforced");
}
Console.WriteLine($"PSD1 native contracts passed: {invalid.Length + 7 + limits.Length} rejected inputs, typed literals, nested arrays, {args.Length} files");
return 0;
