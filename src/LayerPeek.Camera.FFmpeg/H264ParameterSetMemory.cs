namespace LayerPeek.Camera.FFmpeg;

/// <summary>
/// T-207: the latest SPS and PPS seen in the H.264 packets of a stream (Annex B), for a codec context opened later at a key
/// frame (a switch between hardware and software decoding, T-203) when the session description carried none and the camera
/// sent them only at the start of the connection. Not thread-safe (the decoder thread only).
/// </summary>
/// <remarks>
/// Cheap per packet: a packet that starts with a slice is not looked into; any other is scanned only up to its first slice
/// (parameter sets precede it). Each set has a buffer of <see cref="MaxSetSize"/> bytes, allocated when the first one is
/// seen and overwritten only when the content changes; a larger set (untrusted network data, T-050) is ignored and the
/// previous one kept.
/// </remarks>
internal sealed class H264ParameterSetMemory
{
    /// <summary>
    /// Largest SPS or PPS kept, NAL header included. Real ones are a few dozen bytes; even with VUI and every scaling list
    /// they stay well below this.
    /// </summary>
    public const int MaxSetSize = 1024;

    private const int NalSps = 7;
    private const int NalPps = 8;

    /// <summary>Annex B start code written before each set.</summary>
    private static ReadOnlySpan<byte> StartCode => [0, 0, 0, 1];

    private byte[]? _sps;
    private int _spsLength;
    private byte[]? _pps;
    private int _ppsLength;

    /// <summary>Both an SPS and a PPS were seen.</summary>
    public bool IsComplete => _spsLength > 0 && _ppsLength > 0;

    /// <summary>Times a stored set changed (for tests: an unchanged set is not rewritten).</summary>
    public int Changes { get; private set; }

    /// <summary>Bytes <see cref="WriteAnnexB"/> writes; 0 until <see cref="IsComplete"/>.</summary>
    public int AnnexBLength => IsComplete ? (2 * StartCode.Length) + _spsLength + _ppsLength : 0;

    /// <summary>Remembers the SPS and PPS of an Annex B packet, if it has any.</summary>
    public void Observe(ReadOnlySpan<byte> packet)
    {
        int start = NextNal(packet, 0);
        while (start >= 0)
        {
            int type = packet[start] & 0x1F;
            if (type is >= 1 and <= 5)
            {
                // A slice: the parameter sets of this access unit come before it.
                return;
            }

            int next = NextNal(packet, start, out int end);
            if (type == NalSps)
            {
                Store(packet[start..end], ref _sps, ref _spsLength);
            }
            else if (type == NalPps)
            {
                Store(packet[start..end], ref _pps, ref _ppsLength);
            }

            start = next;
        }
    }

    /// <summary>Writes the SPS and PPS as Annex B (start code, SPS, start code, PPS) into <paramref name="destination"/>, of at least <see cref="AnnexBLength"/> bytes.</summary>
    public void WriteAnnexB(Span<byte> destination)
    {
        if (!IsComplete)
        {
            throw new InvalidOperationException("No SPS and PPS were seen.");
        }

        StartCode.CopyTo(destination);
        destination = destination[StartCode.Length..];
        _sps.AsSpan(0, _spsLength).CopyTo(destination);
        destination = destination[_spsLength..];
        StartCode.CopyTo(destination);
        _pps.AsSpan(0, _ppsLength).CopyTo(destination[StartCode.Length..]);
    }

    private void Store(ReadOnlySpan<byte> nal, ref byte[]? buffer, ref int length)
    {
        if (nal.Length > MaxSetSize || (buffer is not null && nal.SequenceEqual(buffer.AsSpan(0, length))))
        {
            return;
        }

        buffer ??= new byte[MaxSetSize];
        nal.CopyTo(buffer);
        length = nal.Length;
        Changes++;
    }

    /// <summary>The first byte of the first NAL unit at or after <paramref name="from"/> (after its start code); -1 when there is none.</summary>
    private static int NextNal(ReadOnlySpan<byte> data, int from)
    {
        int code = StartCodeAt(data, from);
        int start = code < 0 ? -1 : code + 3;
        return start < data.Length ? start : -1;
    }

    /// <summary>
    /// The NAL unit after the one at <paramref name="start"/> (see <see cref="NextNal(ReadOnlySpan{byte}, int)"/>), and in
    /// <paramref name="end"/> where the one at <paramref name="start"/> ends (without the zeros of a 4-byte start code or
    /// trailing padding; a NAL unit always ends with a non-zero byte).
    /// </summary>
    private static int NextNal(ReadOnlySpan<byte> data, int start, out int end)
    {
        int code = StartCodeAt(data, start);
        end = code < 0 ? data.Length : code;
        while (end > start && data[end - 1] == 0)
        {
            end--;
        }

        return code < 0 ? -1 : NextNal(data, code);
    }

    /// <summary>Index of the next three-byte start code (00 00 01) at or after <paramref name="from"/>; -1 when there is none.</summary>
    private static int StartCodeAt(ReadOnlySpan<byte> data, int from)
    {
        int found = data[from..].IndexOf(StartCode[1..]);
        return found < 0 ? -1 : from + found;
    }
}
