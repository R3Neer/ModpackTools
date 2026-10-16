using ModpackTools.Core;

namespace ModpackTools.Cli;

internal sealed class NativeOutputBuffer(string operation, string hint) : MemoryStream
{
    private void Guard(int count)
    { if (count > 64L * 1024 * 1024 - Length) throw new DomainException("Core.OutputLimit", operation + " output exceeds64MiB", hint, "LimitsExceeded"); }
    public override void Write(byte[] buffer, int offset, int count) { Guard(count); base.Write(buffer, offset, count); }
    public override void Write(ReadOnlySpan<byte> buffer) { Guard(buffer.Length); base.Write(buffer); }
}
