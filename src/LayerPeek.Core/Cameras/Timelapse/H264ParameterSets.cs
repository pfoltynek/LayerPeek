namespace LayerPeek.Core.Cameras.Timelapse;

/// <summary>The in-band H.264 parameter sets of an Annex B access unit, without decoding anything.</summary>
internal static class H264ParameterSets
{
    private const int SequenceParameterSet = 7;
    private const int PictureParameterSet = 8;

    /// <summary>
    /// The SPS and PPS NAL units of <paramref name="accessUnit"/> in order, each with its NAL header, without start codes
    /// and concatenated; empty when there are none. Two access units with the same parameter sets give the same bytes.
    /// </summary>
    public static byte[] Extract(ReadOnlySpan<byte> accessUnit)
    {
        int length = 0;
        int position = 0;
        while (NextNal(accessUnit, ref position, out ReadOnlySpan<byte> nal))
        {
            length += IsParameterSet(nal) ? nal.Length : 0;
        }

        if (length == 0)
        {
            return [];
        }

        byte[] sets = new byte[length];
        int written = 0;
        position = 0;
        while (NextNal(accessUnit, ref position, out ReadOnlySpan<byte> nal))
        {
            if (IsParameterSet(nal))
            {
                nal.CopyTo(sets.AsSpan(written));
                written += nal.Length;
            }
        }

        return sets;
    }

    private static bool IsParameterSet(ReadOnlySpan<byte> nal) =>
        nal.Length > 0 && (nal[0] & 0x1F) is SequenceParameterSet or PictureParameterSet;

    /// <summary>The next NAL unit after a 3- or 4-byte start code from <paramref name="position"/>, without the start code.</summary>
    private static bool NextNal(ReadOnlySpan<byte> data, ref int position, out ReadOnlySpan<byte> nal)
    {
        ReadOnlySpan<byte> startCode = [0, 0, 1];
        int found = position < data.Length ? data[position..].IndexOf(startCode) : -1;
        if (found < 0)
        {
            nal = default;
            position = data.Length;
            return false;
        }

        int begin = position + found + startCode.Length;
        int next = data[begin..].IndexOf(startCode);
        int end = next < 0 ? data.Length : begin + next;
        // The leading zero of a following 4-byte start code and trailing zeros are not part of a NAL unit (it ends with
        // its stop bit, so its last byte is never 0).
        nal = data[begin..end].TrimEnd((byte)0);
        position = end;
        return true;
    }
}
