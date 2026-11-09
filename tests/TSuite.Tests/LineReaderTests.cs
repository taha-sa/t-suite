using System.Text;
using TSuite.Link;

namespace TSuite.Tests;

public class LineReaderTests
{
    sealed class ChunkStream(byte[] data, int chunk) : Stream
    {
        int _pos;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            int n = Math.Min(Math.Min(chunk, buffer.Length), data.Length - _pos);
            data.AsMemory(_pos, n).CopyTo(buffer); _pos += n;
            return ValueTask.FromResult(n);
        }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin so) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Fragmented_large_line_and_following_lines_are_read_intact()
    {
        var big = new string('x', LinkProtocol.MaxLineBytes - 16);
        var data = Encoding.UTF8.GetBytes("a\n" + big + "\nb\n\nlast\n");
        using var r = new LineReader(new ChunkStream(data, 997));
        async Task<string?> Next() => await r.ReadLineAsync(default) is { } l ? Encoding.UTF8.GetString(l.Span) : null;
        Assert.Equal("a", await Next());
        Assert.Equal(big, await Next());
        Assert.Equal("b", await Next());
        Assert.Equal("", await Next());
        Assert.Equal("last", await Next());
        Assert.Null(await Next());
    }

    [Fact]
    public async Task Over_limit_line_still_throws_when_fragmented()
    {
        var data = Encoding.UTF8.GetBytes(new string('y', LinkProtocol.MaxLineBytes + 100) + "\n");
        using var r = new LineReader(new ChunkStream(data, 4096));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await r.ReadLineAsync(default));
    }
}
