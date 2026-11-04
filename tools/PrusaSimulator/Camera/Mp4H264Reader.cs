using System.Buffers.Binary;

namespace LayerPeek.Simulator.Camera;

/// <summary>
/// The H.264 video track of an MP4 file (ISO BMFF) as an Annex B stream, so that <see cref="H264Clip"/> can loop a
/// recording (T-155, <c>--camera-clip</c>): LayerPeek's own recordings and dashcam saves (fragmented MP4, T-144) as well
/// as ordinary MP4 files with a sample table. Only what a clip needs: the first <c>avc1</c> track, its parameter sets
/// (<c>avcC</c>) and its samples in decoding order; no edit lists, no timing (the camera plays at <c>--fps</c>).
/// </summary>
internal static class Mp4H264Reader
{
    private static readonly byte[] StartCode = [0, 0, 0, 1];

    /// <summary>Whether the data starts like an MP4 file (an <c>ftyp</c> box).</summary>
    public static bool IsMp4(ReadOnlySpan<byte> data) => data.Length >= 8 && data.Slice(4, 4).SequenceEqual("ftyp"u8);

    /// <exception cref="InvalidDataException">No H.264 track, or the boxes are broken.</exception>
    public static byte[] ToAnnexB(ReadOnlySpan<byte> file)
    {
        try
        {
            return Convert(file);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
        {
            // A box shorter than its fields, or a count or size beyond what the file can hold: the file is broken.
            throw new InvalidDataException("The MP4 file is broken.", ex);
        }
    }

    private static byte[] Convert(ReadOnlySpan<byte> file)
    {
        if (!TryFind(file, "moov"u8, out ReadOnlySpan<byte> moov))
        {
            throw new InvalidDataException("The MP4 file has no movie header (moov).");
        }

        Track track = FindVideoTrack(moov, file.Length) ?? throw new InvalidDataException("The MP4 file has no H.264 video track.");
        using MemoryStream annexB = new();
        foreach (byte[] parameterSet in track.ParameterSets)
        {
            annexB.Write(StartCode);
            annexB.Write(parameterSet);
        }

        int samples = 0;
        long total = 0;
        foreach ((long offset, int size) in track.Samples.Count > 0 ? track.Samples : FragmentSamples(file, track))
        {
            if (offset < 0 || size < 0 || offset + size > file.Length)
            {
                throw new InvalidDataException("An MP4 sample lies outside the file.");
            }

            // Samples never overlap: more than the file holds would only repeat its bytes (and could take any memory).
            total += size;
            if (total > file.Length)
            {
                throw new InvalidDataException("The MP4 samples overlap.");
            }

            WriteSample(annexB, file.Slice((int)offset, size), track.LengthSize);
            samples++;
        }

        if (samples == 0)
        {
            throw new InvalidDataException("The H.264 track of the MP4 file has no samples.");
        }

        return annexB.ToArray();
    }

    /// <summary>One sample: NAL units, each behind a big-endian length of <paramref name="lengthSize"/> bytes.</summary>
    private static void WriteSample(MemoryStream annexB, ReadOnlySpan<byte> sample, int lengthSize)
    {
        while (sample.Length >= lengthSize)
        {
            long length = 0;
            for (int i = 0; i < lengthSize; i++)
            {
                length = (length << 8) | sample[i];
            }

            sample = sample[lengthSize..];
            if (length > sample.Length)
            {
                throw new InvalidDataException("An H.264 NAL unit of the MP4 file is cut off.");
            }

            if (length > 0)
            {
                annexB.Write(StartCode);
                annexB.Write(sample[..(int)length]);
            }

            sample = sample[(int)length..];
        }
    }

    private static Track? FindVideoTrack(ReadOnlySpan<byte> moov, long fileLength)
    {
        uint defaultSize = 0;
        foreach (Box trak in Children(moov))
        {
            if (!trak.Is("trak"u8))
            {
                continue;
            }

            ReadOnlySpan<byte> body = moov.Slice(trak.BodyStart, trak.BodyLength);
            if (!TryFind(body, "tkhd"u8, out ReadOnlySpan<byte> tkhd)
                || !TryFindPath(body, out ReadOnlySpan<byte> stbl, "mdia"u8, "minf"u8, "stbl"u8)
                || !TryFind(stbl, "stsd"u8, out ReadOnlySpan<byte> stsd)
                || ReadAvcConfiguration(stsd) is not { } config)
            {
                continue;
            }

            uint trackId = BinaryPrimitives.ReadUInt32BigEndian(tkhd[(tkhd[0] == 1 ? 20 : 12)..]);
            if (TryFindPath(moov, out ReadOnlySpan<byte> mvex, "mvex"u8))
            {
                foreach (Box trex in Children(mvex))
                {
                    ReadOnlySpan<byte> trexBody = mvex.Slice(trex.BodyStart, trex.BodyLength);
                    if (trex.Is("trex"u8) && trexBody.Length >= 24 && BinaryPrimitives.ReadUInt32BigEndian(trexBody[4..]) == trackId)
                    {
                        defaultSize = BinaryPrimitives.ReadUInt32BigEndian(trexBody[16..]);
                    }
                }
            }

            return new Track(trackId, config.LengthSize, config.ParameterSets, SampleTable(stbl, fileLength), defaultSize);
        }

        return null;
    }

    /// <summary>The <c>avcC</c> of the first <c>avc1</c> / <c>avc3</c> sample entry: NAL length size, SPS and PPS.</summary>
    private static (int LengthSize, List<byte[]> ParameterSets)? ReadAvcConfiguration(ReadOnlySpan<byte> stsd)
    {
        // Full box header (4) and entry count (4), then the sample entries.
        ReadOnlySpan<byte> entries = stsd.Length > 8 ? stsd[8..] : [];
        foreach (Box entry in Children(entries))
        {
            if (!entry.Is("avc1"u8) && !entry.Is("avc3"u8))
            {
                continue;
            }

            // VisualSampleEntry: 78 bytes before its child boxes.
            ReadOnlySpan<byte> visual = entries.Slice(entry.BodyStart, entry.BodyLength);
            if (visual.Length <= 78 || !TryFind(visual[78..], "avcC"u8, out ReadOnlySpan<byte> avcC) || avcC.Length < 7)
            {
                continue;
            }

            int lengthSize = (avcC[4] & 0x03) + 1;
            List<byte[]> sets = [];
            int position = 5;
            int spsCount = avcC[position++] & 0x1F;
            for (int i = 0; i < spsCount; i++)
            {
                sets.Add(ReadParameterSet(avcC, ref position));
            }

            int ppsCount = position < avcC.Length ? avcC[position++] : 0;
            for (int i = 0; i < ppsCount; i++)
            {
                sets.Add(ReadParameterSet(avcC, ref position));
            }

            return (lengthSize, sets);
        }

        return null;
    }

    private static byte[] ReadParameterSet(ReadOnlySpan<byte> avcC, ref int position)
    {
        if (position + 2 > avcC.Length)
        {
            throw new InvalidDataException("The H.264 configuration (avcC) of the MP4 file is cut off.");
        }

        int length = BinaryPrimitives.ReadUInt16BigEndian(avcC[position..]);
        position += 2;
        if (position + length > avcC.Length)
        {
            throw new InvalidDataException("The H.264 configuration (avcC) of the MP4 file is cut off.");
        }

        byte[] set = avcC.Slice(position, length).ToArray();
        position += length;
        return set;
    }

    /// <summary>Offsets and sizes from the sample table (empty in a fragmented file, whose samples are in fragments).</summary>
    private static List<(long Offset, int Size)> SampleTable(ReadOnlySpan<byte> stbl, long fileLength)
    {
        List<(long, int)> samples = [];
        if (!TryFind(stbl, "stsz"u8, out ReadOnlySpan<byte> stsz) || !TryFind(stbl, "stsc"u8, out ReadOnlySpan<byte> stsc))
        {
            return samples;
        }

        bool wide = TryFind(stbl, "co64"u8, out ReadOnlySpan<byte> chunks);
        if (!wide && !TryFind(stbl, "stco"u8, out chunks))
        {
            return samples;
        }

        uint uniformSize = BinaryPrimitives.ReadUInt32BigEndian(stsz[4..]);
        int sampleCount = checked((int)BinaryPrimitives.ReadUInt32BigEndian(stsz[8..]));
        int chunkCount = checked((int)BinaryPrimitives.ReadUInt32BigEndian(chunks[4..]));
        int ruleCount = checked((int)BinaryPrimitives.ReadUInt32BigEndian(stsc[4..]));
        // No more samples than the table lists or the file holds (a broken count must not take any memory).
        if (uniformSize == 0 ? sampleCount > (stsz.Length - 12) / 4 : sampleCount > fileLength / uniformSize)
        {
            throw new InvalidDataException("The MP4 sample table lists more samples than it holds.");
        }
        int sample = 0;
        for (int rule = 0; rule < ruleCount && sample < sampleCount; rule++)
        {
            ReadOnlySpan<byte> entry = stsc.Slice(8 + (rule * 12), 12);
            int firstChunk = (int)BinaryPrimitives.ReadUInt32BigEndian(entry) - 1;
            int perChunk = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[4..]);
            int endChunk = rule + 1 < ruleCount ? (int)BinaryPrimitives.ReadUInt32BigEndian(stsc[(8 + ((rule + 1) * 12))..]) - 1 : chunkCount;
            for (int chunk = firstChunk; chunk < endChunk && sample < sampleCount; chunk++)
            {
                long offset = wide
                    ? (long)BinaryPrimitives.ReadUInt64BigEndian(chunks[(8 + (chunk * 8))..])
                    : BinaryPrimitives.ReadUInt32BigEndian(chunks[(8 + (chunk * 4))..]);
                for (int i = 0; i < perChunk && sample < sampleCount; i++, sample++)
                {
                    int size = checked((int)(uniformSize != 0 ? uniformSize : BinaryPrimitives.ReadUInt32BigEndian(stsz[(12 + (sample * 4))..])));
                    samples.Add((offset, size));
                    offset += size;
                }
            }
        }

        return samples;
    }

    /// <summary>The samples of <paramref name="track"/> in the movie fragments (<c>moof</c> / <c>traf</c> / <c>trun</c>).</summary>
    private static List<(long Offset, int Size)> FragmentSamples(ReadOnlySpan<byte> file, Track track)
    {
        List<(long, int)> samples = [];
        foreach (Box moof in Children(file))
        {
            if (!moof.Is("moof"u8))
            {
                continue;
            }

            ReadOnlySpan<byte> fragment = file.Slice(moof.BodyStart, moof.BodyLength);
            foreach (Box traf in Children(fragment))
            {
                ReadOnlySpan<byte> body = fragment.Slice(traf.BodyStart, traf.BodyLength);
                if (!traf.Is("traf"u8) || !TryFind(body, "tfhd"u8, out ReadOnlySpan<byte> tfhd)
                    || BinaryPrimitives.ReadUInt32BigEndian(tfhd[4..]) != track.Id)
                {
                    continue;
                }

                uint tfhdFlags = BinaryPrimitives.ReadUInt32BigEndian(tfhd) & 0xFFFFFF;
                int position = 8;
                // Without an explicit base the data offsets count from the start of the moof (default-base-is-moof, and
                // what every writer of a single track does).
                long baseOffset = moof.Start;
                if ((tfhdFlags & 0x01) != 0)
                {
                    baseOffset = (long)BinaryPrimitives.ReadUInt64BigEndian(tfhd[position..]);
                    position += 8;
                }

                position += (tfhdFlags & 0x02) != 0 ? 4 : 0;
                position += (tfhdFlags & 0x08) != 0 ? 4 : 0;
                uint defaultSize = track.DefaultSampleSize;
                if ((tfhdFlags & 0x10) != 0)
                {
                    defaultSize = BinaryPrimitives.ReadUInt32BigEndian(tfhd[position..]);
                }

                foreach (Box trun in Children(body))
                {
                    if (trun.Is("trun"u8))
                    {
                        ReadRun(body.Slice(trun.BodyStart, trun.BodyLength), baseOffset, defaultSize, file.Length, samples);
                    }
                }
            }
        }

        return samples;
    }

    private static void ReadRun(ReadOnlySpan<byte> trun, long baseOffset, uint defaultSize, long fileLength, List<(long, int)> samples)
    {
        uint flags = BinaryPrimitives.ReadUInt32BigEndian(trun) & 0xFFFFFF;
        int count = checked((int)BinaryPrimitives.ReadUInt32BigEndian(trun[4..]));
        int position = 8;
        long offset = baseOffset;
        if ((flags & 0x01) != 0)
        {
            offset += BinaryPrimitives.ReadInt32BigEndian(trun[position..]);
            position += 4;
        }

        position += (flags & 0x04) != 0 ? 4 : 0;
        // No more samples than the run describes or the file holds (a broken count must not take any memory).
        int perSample = 4 * System.Numerics.BitOperations.PopCount(flags & 0xF00);
        if (perSample > 0 ? count > (trun.Length - position) / perSample : (defaultSize == 0 ? count > 0 : count > fileLength / defaultSize))
        {
            throw new InvalidDataException("An MP4 track run lists more samples than it holds.");
        }

        for (int i = 0; i < count; i++)
        {
            position += (flags & 0x100) != 0 ? 4 : 0;
            uint size = defaultSize;
            if ((flags & 0x200) != 0)
            {
                size = BinaryPrimitives.ReadUInt32BigEndian(trun[position..]);
                position += 4;
            }

            position += (flags & 0x400) != 0 ? 4 : 0;
            position += (flags & 0x800) != 0 ? 4 : 0;
            samples.Add((offset, checked((int)size)));
            offset += size;
        }
    }

    private static bool TryFindPath(ReadOnlySpan<byte> data, out ReadOnlySpan<byte> body, ReadOnlySpan<byte> first, ReadOnlySpan<byte> second = default, ReadOnlySpan<byte> third = default)
    {
        body = default;
        if (!TryFind(data, first, out ReadOnlySpan<byte> level))
        {
            return false;
        }

        if (!second.IsEmpty && !TryFind(level, second, out level))
        {
            return false;
        }

        if (!third.IsEmpty && !TryFind(level, third, out level))
        {
            return false;
        }

        body = level;
        return true;
    }

    /// <summary>The body of the first child box of <paramref name="type"/>.</summary>
    private static bool TryFind(ReadOnlySpan<byte> data, ReadOnlySpan<byte> type, out ReadOnlySpan<byte> body)
    {
        foreach (Box box in Children(data))
        {
            if (box.Is(type))
            {
                body = data.Slice(box.BodyStart, box.BodyLength);
                return true;
            }
        }

        body = default;
        return false;
    }

    /// <summary>The boxes one after another in <paramref name="data"/>; stops at a box that does not fit.</summary>
    private static List<Box> Children(ReadOnlySpan<byte> data)
    {
        List<Box> boxes = [];
        int position = 0;
        while (position + 8 <= data.Length)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(data[position..]);
            int header = 8;
            if (size == 1)
            {
                if (position + 16 > data.Length)
                {
                    break;
                }

                size = (long)BinaryPrimitives.ReadUInt64BigEndian(data[(position + 8)..]);
                header = 16;
            }
            else if (size == 0)
            {
                // To the end of the enclosing data.
                size = data.Length - position;
            }

            if (size < header || position + size > data.Length)
            {
                break;
            }

            uint type = BinaryPrimitives.ReadUInt32BigEndian(data[(position + 4)..]);
            boxes.Add(new Box(type, position, position + header, (int)size - header));
            position += (int)size;
        }

        return boxes;
    }

    private readonly record struct Box(uint Type, int Start, int BodyStart, int BodyLength)
    {
        public bool Is(ReadOnlySpan<byte> type) => type.Length == 4 && Type == BinaryPrimitives.ReadUInt32BigEndian(type);
    }

    private sealed record Track(uint Id, int LengthSize, List<byte[]> ParameterSets, List<(long Offset, int Size)> Samples, uint DefaultSampleSize);
}
