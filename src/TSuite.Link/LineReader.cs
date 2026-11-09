using System.Buffers;

namespace TSuite.Link;

/// <summary>Akistan '\n' ile ayrilmis satirlari ArrayPool tamponuyla okur.</summary>
public sealed class LineReader : IDisposable
{
    readonly Stream _s;
    byte[] _buf = ArrayPool<byte>.Shared.Rent(8192);
    int _start, _end;
    // Taranmis ust sinir: [_start,_scanned) satir sonu icermez; ayni satir icin tekrar taranmaz.
    int _scanned;

    public LineReader(Stream s) { _s = s; }

    /// <summary>Bir satir (\n'siz) doner; akis biterse null. Gecerlilik: sonraki cagriya kadar.</summary>
    public async ValueTask<ReadOnlyMemory<byte>?> ReadLineAsync(CancellationToken ct)
    {
        while (true)
        {
            if (_scanned < _start) _scanned = _start;
            int idx = Array.IndexOf(_buf, (byte)'\n', _scanned, _end - _scanned);
            if (idx >= 0)
            {
                var line = new ReadOnlyMemory<byte>(_buf, _start, idx - _start);
                _start = idx + 1;
                _scanned = _start;
                return line;
            }
            _scanned = _end;
            if (_end - _start >= LinkProtocol.MaxLineBytes) throw new InvalidDataException("line too long");
            if (_end == _buf.Length)
            {
                if (_start > 0)
                {
                    Buffer.BlockCopy(_buf, _start, _buf, 0, _end - _start);
                    _scanned -= _start; _end -= _start; _start = 0;
                }
                else
                {
                    var nb = ArrayPool<byte>.Shared.Rent(_buf.Length * 2);
                    Buffer.BlockCopy(_buf, 0, nb, 0, _end);
                    ArrayPool<byte>.Shared.Return(_buf);
                    _buf = nb;
                }
            }
            int n = await _s.ReadAsync(_buf.AsMemory(_end), ct).ConfigureAwait(false);
            if (n == 0) return null;
            _end += n;
        }
    }

    public void Dispose() { ArrayPool<byte>.Shared.Return(_buf); _buf = []; }
}
