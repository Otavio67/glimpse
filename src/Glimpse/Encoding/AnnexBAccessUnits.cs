namespace Glimpse.Encoding;
// Access units are delimited by AUD NAL units inserted by FFmpeg's h264_metadata filter.
public sealed class AnnexBAccessUnits
{
    private readonly List<byte> _pending = new();
    private readonly List<byte> _prefix = new();
    public IEnumerable<byte[]> Push(ReadOnlySpan<byte> bytes)
    {
        _pending.AddRange(bytes.ToArray());
        if (_pending.Count > 16 * 1024 * 1024) throw new InvalidDataException("H.264 access unit exceeds limit.");
        var ready = new List<byte[]>();
        int first = FindAud(0);
        if (first > 0) _pending.RemoveRange(0, first);
        while ((first = FindAud(0)) >= 0) {
            int next = FindAud(first + 4); if (next < 0) break;
            var unit = _pending.GetRange(first, next - first).ToArray();
            if (ContainsVcl(unit)) { if (_prefix.Count > 0) { _prefix.AddRange(unit); ready.Add(_prefix.ToArray()); _prefix.Clear(); } else ready.Add(unit); }
            else if (ContainsNonAud(unit)) _prefix.AddRange(unit);
            _pending.RemoveRange(0, next);
        }
        return ready;
    }
    private static bool ContainsVcl(byte[] unit) => NalTypes(unit).Any(type => type is >= 1 and <= 5);
    private static bool ContainsNonAud(byte[] unit) => NalTypes(unit).Any(type => type != 9);
    private static IEnumerable<int> NalTypes(byte[] data) {
        for (int i=0; i+4<data.Length; i++) {
            int header = data[i]==0 && data[i+1]==0 && data[i+2]==0 && data[i+3]==1 ? i+4 : data[i]==0 && data[i+1]==0 && data[i+2]==1 ? i+3 : -1;
            if (header>=0 && header<data.Length) { yield return data[header] & 31; i=header; }
        }
    }
    private int FindAud(int from) {
        for (int i = from; i + 4 < _pending.Count; i++) {
            if (_pending[i] != 0 || _pending[i+1] != 0) continue;
            if (_pending[i+2] == 1 && (_pending[i+3] & 31) == 9) return i;
            if (_pending[i+2] == 0 && _pending[i+3] == 1 && (_pending[i+4] & 31) == 9) return i;
        }
        return -1;
    }
}

