namespace PrusaTrayDiagnostic.Simulator.Camera;

/// <summary>
/// A raw H.264 elementary stream (Annex B) split into access units (frames) of NAL units without start codes, prepared
/// for seamless looping: <see cref="LoopStart"/> is the first frame with an IDR slice, and that frame always carries SPS
/// and PPS (copied from elsewhere in the clip when the recording has them only once), so that a decoder can restart at
/// every loop without a gap.
/// </summary>
public sealed class H264Clip
{
    private const int NalSlice = 1;
    private const int NalIdr = 5;
    private const int NalSps = 7;
    private const int NalPps = 8;

    private H264Clip(IReadOnlyList<IReadOnlyList<byte[]>> frames, int loopStart)
    {
        Frames = frames;
        LoopStart = loopStart;
    }

    /// <summary>Access units in decoding order; each is a list of NAL units (first byte = NAL header).</summary>
    public IReadOnlyList<IReadOnlyList<byte[]>> Frames { get; }

    /// <summary>Index of the first frame with an IDR slice; frames before it cannot be decoded and are never sent.</summary>
    public int LoopStart { get; }

    /// <summary>Frames played per loop (<see cref="Frames"/> from <see cref="LoopStart"/>).</summary>
    public int LoopLength => Frames.Count - LoopStart;

    /// <exception cref="IOException">The file cannot be read.</exception>
    /// <exception cref="InvalidDataException">The file is not an H.264 Annex B stream with SPS, PPS and an IDR frame.</exception>
    public static H264Clip Load(string path) => Parse(File.ReadAllBytes(path));

    /// <exception cref="InvalidDataException">The data is not an H.264 Annex B stream with SPS, PPS and an IDR frame.</exception>
    public static H264Clip Parse(ReadOnlySpan<byte> annexB)
    {
        List<byte[]> nals = SplitAnnexB(annexB);
        List<List<byte[]>> frames = [];
        List<byte[]>? current = null;
        bool hasSlice = false;
        byte[]? sps = null;
        byte[]? pps = null;
        foreach (byte[] nal in nals)
        {
            int type = nal[0] & 0x1F;
            sps ??= type == NalSps ? nal : null;
            pps ??= type == NalPps ? nal : null;
            if (current is null || (hasSlice && StartsAccessUnit(nal)))
            {
                current = [];
                frames.Add(current);
                hasSlice = false;
            }

            current.Add(nal);
            hasSlice |= IsVcl(nal);
        }

        // A trailing group without a slice (e.g. a lone SPS) is not a frame.
        if (frames.Count > 0 && !frames[^1].Exists(IsVcl))
        {
            frames.RemoveAt(frames.Count - 1);
        }

        int loopStart = frames.FindIndex(static f => f.Exists(static n => (n[0] & 0x1F) == NalIdr));
        if (loopStart < 0 || sps is null || pps is null)
        {
            throw new InvalidDataException("The clip is not an H.264 Annex B stream with SPS, PPS and an IDR frame.");
        }

        List<byte[]> first = frames[loopStart];
        if (!first.Exists(static n => (n[0] & 0x1F) == NalPps))
        {
            first.Insert(0, pps);
        }

        if (!first.Exists(static n => (n[0] & 0x1F) == NalSps))
        {
            first.Insert(0, sps);
        }

        return new H264Clip(frames.ConvertAll(static f => (IReadOnlyList<byte[]>)f.AsReadOnly()).AsReadOnly(), loopStart);
    }

    private static bool IsVcl(byte[] nal) => (nal[0] & 0x1F) is NalSlice or NalIdr;

    /// <summary>Whether the NAL unit starts a new access unit, given that the current one already has a slice.</summary>
    private static bool StartsAccessUnit(byte[] nal) =>
        !IsVcl(nal) || (nal.Length > 1 && (nal[1] & 0x80) != 0); // first_mb_in_slice == 0 (ue(v) "1")

    private static List<byte[]> SplitAnnexB(ReadOnlySpan<byte> data)
    {
        List<byte[]> nals = [];
        int start = -1;
        int i = 0;
        while (i + 3 <= data.Length)
        {
            if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 1)
            {
                AddNal(nals, data, start, i);
                i += 3;
                start = i;
                continue;
            }

            i++;
        }

        AddNal(nals, data, start, data.Length);
        return nals;
    }

    private static void AddNal(List<byte[]> nals, ReadOnlySpan<byte> data, int start, int end)
    {
        if (start < 0)
        {
            return;
        }

        // Zeros before the next start code belong to it (4-byte start code) or are trailing padding.
        while (end > start && data[end - 1] == 0)
        {
            end--;
        }

        if (end > start)
        {
            nals.Add(data[start..end].ToArray());
        }
    }
}
