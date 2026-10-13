namespace ModpackTools.Core.Data;

// Guard read-ahead before JsonTextReader receives characters. The wrapped reader
// is caller-owned; this wrapper neither buffers a complete token nor closes it.
internal sealed class TokenLengthReader(TextReader source, int maximumCharacters) : TextReader
{
    private char quote;
    private bool escaped;
    private bool slash;
    private bool lineComment;
    private bool blockComment;
    private bool star;
    private int length;
    public override int Peek() => source.Peek();
    public override int Read()
    {
        var value = source.Read();
        if (value >= 0) Check((char)value);
        return value;
    }
    public override int Read(char[] buffer, int index, int count) => Read(buffer.AsSpan(index, count));
    public override int Read(Span<char> buffer)
    {
        var count = source.Read(buffer);
        foreach (var value in buffer[..count]) Check(value);
        return count;
    }
    private void Check(char value)
    {
        if (lineComment)
        {
            if (value is '\r' or '\n') { lineComment = false; length = 0; }
            else Increment();
            return;
        }
        if (blockComment)
        {
            if (star && value == '/') { blockComment = false; star = false; length = 0; }
            else
            {
                if (star) Increment();
                star = value == '*';
                if (!star) Increment();
            }
            return;
        }
        if (quote != '\0')
        {
            if (!escaped && value == quote) { quote = '\0'; length = 0; return; }
            Increment();
            if (escaped) escaped = false;
            else escaped = value == '\\';
            return;
        }
        if (slash)
        {
            slash = false;
            if (value == '/') { lineComment = true; length = 0; return; }
            if (value == '*') { blockComment = true; length = 0; return; }
        }
        if (value == '/') { slash = true; length = 0; }
        else if (value is '"' or '\'') { quote = value; length = 0; }
        else if (char.IsWhiteSpace(value) || value is '{' or '}' or '[' or ']' or ':' or ',' or '(' or ')') length = 0;
        else Increment();
    }
    private void Increment()
    {
        if (length == maximumCharacters) throw new CacheJsonLimitException("Lexical token length exceeds the reader quota.");
        length++;
    }
}
