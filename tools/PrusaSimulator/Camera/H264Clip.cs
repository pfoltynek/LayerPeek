namespace LayerPeek.Simulator.Camera;

/// <summary>
/// A raw H.264 elementary stream (Annex B) split into access units (frames) of NAL units without start codes, prepared
/// for seamless looping: <see cref="LoopStart"/> is the first frame with an IDR slice, and every frame with an IDR slice
/// carries SPS and PPS (copied from elsewhere in the clip when the recording has them only once), so that a decoder can
/// restart at every loop and at every key frame without a gap.
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

    /// <summary>
    /// A raw H.264 stream (Annex B, e.g. <c>.h264</c>) or the H.264 track of an MP4 file (T-155: LayerPeek's recordings
    /// and dashcam saves, or any other MP4 with H.264), told apart by the content, not the extension.
    /// </summary>
    /// <exception cref="IOException">The file cannot be read.</exception>
    /// <exception cref="InvalidDataException">The file is neither an H.264 Annex B stream nor an MP4 file with an H.264 track, each with SPS, PPS and an IDR frame.</exception>
    public static H264Clip Load(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        return Parse(Mp4H264Reader.IsMp4(data) ? Mp4H264Reader.ToAnnexB(data) : data);
    }

    /// <param name="annexB">The stream.</param>
    /// <param name="repeatParameterSets">
    /// Put SPS and PPS in front of every IDR frame (default, like the Buddy3D). Off only for tests (T-207): the frames stay
    /// as recorded, e.g. a camera that sends the parameter sets once at the start of the connection; then a client cannot
    /// start at a later key frame (or at a loop) on its own.
    /// </param>
    /// <exception cref="InvalidDataException">The data is not an H.264 Annex B stream with SPS, PPS and an IDR frame.</exception>
    public static H264Clip Parse(ReadOnlySpan<byte> annexB, bool repeatParameterSets = true)
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

        // Like the Buddy3D (T-205): SPS and PPS in front of every IDR frame, not only at the loop start, so that a client
        // which opens a new decoder at a later key frame (LayerPeek switching between hardware and software decoding,
        // T-203) can decode it; an MP4 clip has them only once (avcC). A frame without them gets the latest ones before it
        // in decoding order (at the loop start: the first ones of the clip).
        for (int i = repeatParameterSets ? loopStart : frames.Count; i < frames.Count; i++)
        {
            List<byte[]> frame = frames[i];
            byte[]? ownSps = frame.Find(static n => (n[0] & 0x1F) == NalSps);
            byte[]? ownPps = frame.Find(static n => (n[0] & 0x1F) == NalPps);
            sps = ownSps ?? sps;
            pps = ownPps ?? pps;
            if (!frame.Exists(static n => (n[0] & 0x1F) == NalIdr))
            {
                continue;
            }

            if (ownPps is null)
            {
                // After the SPS it refers to.
                frame.Insert(ownSps is null ? 0 : frame.IndexOf(ownSps) + 1, pps);
            }

            if (ownSps is null)
            {
                frame.Insert(0, sps);
            }
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
