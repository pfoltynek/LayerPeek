using System.Buffers.Binary;

using LayerPeek.Simulator.Camera;

namespace LayerPeek.Simulator.Tests;

/// <summary>
/// T-155: <c>--camera-clip</c> plays MP4 files too (LayerPeek's fragmented recordings and ordinary MP4 with a sample
/// table): the H.264 track becomes the same frames as the raw Annex B clip. The MP4 files are written here from the
/// Buddy3D fixture (nothing new in the repository); a real LayerPeek recording is read in LayerPeek.Camera.FFmpeg.Tests.
/// </summary>
public sealed class Mp4ClipTests : IDisposable
{
    private static readonly H264Clip Buddy3D = H264Clip.Load(SimulatorHost.DefaultClipPath);

    private readonly string _folder = Directory.CreateTempSubdirectory("LayerPeek.Simulator.Mp4-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnMp4FileIsTheSameClipAsItsRawStream(bool fragmented)
    {
        string path = Path.Combine(_folder, "clip.mp4");
        File.WriteAllBytes(path, TestMp4.FromClip(Buddy3D, fragmented));

        H264Clip clip = H264Clip.Load(path);

        Assert.Equal(Buddy3D.LoopStart, clip.LoopStart);
        Assert.Equal(Buddy3D.Frames.Count, clip.Frames.Count);
        for (int f = 0; f < clip.Frames.Count; f++)
        {
            // The first frame gets SPS and PPS from the avcC (the samples carry them too, as the Buddy3D sends them).
            byte[][] expected = [.. Buddy3D.Frames[f]];
            byte[][] actual = [.. clip.Frames[f].Skip(clip.Frames[f].Count - expected.Length)];
            Assert.Equal(expected.Length, actual.Length);
            for (int n = 0; n < expected.Length; n++)
            {
                Assert.Equal(expected[n], actual[n]);
            }
        }
    }

    [Fact]
    public async Task TheHostPlaysAnMp4GivenAsCameraClip()
    {
        string path = Path.Combine(_folder, "recording.mp4");
        File.WriteAllBytes(path, TestMp4.FromClip(Buddy3D, fragmented: true));
        Assert.True(SimulatorOptions.TryParse(["--camera-clip", path, "--port", "0", "--camera-port", "0"], out SimulatorOptions options, out string? error), error);

        await using SimulatorHost host = await SimulatorHost.StartAsync(options, TestContext.Current.CancellationToken);

        // Started at all: an unreadable clip throws.
        Assert.NotNull(host.Camera);
    }

    [Fact]
    public void TheOlderNameOfTheOptionStillWorks()
    {
        Assert.True(SimulatorOptions.TryParse(["--clip", "old.h264"], out SimulatorOptions options, out _));
        Assert.Equal("old.h264", options.ClipPath);
    }

    [Fact]
    public void AnMp4WithoutAnH264TrackIsRejected()
    {
        string path = Path.Combine(_folder, "audio.mp4");
        byte[] ftyp = TestMp4.Box("ftyp", "isom"u8.ToArray(), new byte[4], "isom"u8.ToArray());
        File.WriteAllBytes(path, [.. ftyp, .. TestMp4.Box("moov", TestMp4.Box("mvhd", new byte[100]))]);

        Assert.Throws<InvalidDataException>(() => H264Clip.Load(path));
        File.WriteAllBytes(path, ftyp);
        Assert.Throws<InvalidDataException>(() => H264Clip.Load(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABrokenMp4IsRejectedAsInvalidData(bool fragmented)
    {
        // Seeded corruption of a valid file (cut off, bytes overwritten, also with values of extreme counts and sizes):
        // a clip file is the user's input, so it may be anything, and it is told as broken, not as a crash.
        byte[] valid = TestMp4.FromClip(Buddy3D, fragmented);
        Random random = new(fragmented ? 2 : 1);
        byte[][] extremes = [[0xFF, 0xFF, 0xFF, 0xFF], [0x7F, 0xFF, 0xFF, 0xFF], [0x80, 0, 0, 0], [0, 0, 0, 0], [0, 0, 0, 1]];
        for (int i = 0; i < 600; i++)
        {
            byte[] broken = i % 3 == 0 ? valid[..random.Next(8, valid.Length)] : [.. valid];
            if (i % 3 != 0)
            {
                // Mostly the boxes before the media data: sizes, counts, flags, offsets.
                int position = random.Next(i % 2 == 0 ? Math.Min(valid.Length - 4, 2048) : valid.Length - 4);
                extremes[random.Next(extremes.Length)].CopyTo(broken, position);
            }

            try
            {
                H264Clip.Parse(Mp4H264Reader.ToAnnexB(broken));
            }
            catch (InvalidDataException)
            {
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AHugeSampleCountTakesNoMemory(bool fragmented)
    {
        // A count of 2^31 - 1 samples of the default / uniform size, with nothing behind it.
        byte[] ftyp = TestMp4.Box("ftyp", "isom"u8.ToArray(), new byte[4], "isom"u8.ToArray());
        byte[] avc1 = TestMp4.Box("avc1", new byte[78], TestMp4.Box("avcC", [1, 0x42, 0, 0x1E, 0xFF, 0xE0, 0]));
        byte[] stsd = TestMp4.Box("stsd", new byte[4], [0, 0, 0, 1], avc1);
        byte[] tkhd = TestMp4.Box("tkhd", new byte[4], new byte[8], [0, 0, 0, 1], new byte[68]);
        byte[] huge = [0x7F, 0xFF, 0xFF, 0xFF];
        byte[] file = fragmented
            ? [.. ftyp,
                .. TestMp4.Box("moov",
                    TestMp4.Box("trak", tkhd, TestMp4.Box("mdia", TestMp4.Box("minf", TestMp4.Box("stbl", stsd)))),
                    TestMp4.Box("mvex", TestMp4.Box("trex", new byte[4], [0, 0, 0, 1], [0, 0, 0, 1], new byte[4], [0, 0, 0, 1], new byte[4]))),
                .. TestMp4.Box("moof", TestMp4.Box("traf", TestMp4.Box("tfhd", [0, 0x02, 0, 0], [0, 0, 0, 1]), TestMp4.Box("trun", new byte[4], huge)))]
            : [.. ftyp,
                .. TestMp4.Box("moov", TestMp4.Box("trak", tkhd, TestMp4.Box("mdia", TestMp4.Box("minf", TestMp4.Box("stbl",
                    stsd,
                    TestMp4.Box("stsc", new byte[4], [0, 0, 0, 1], [0, 0, 0, 1], huge, [0, 0, 0, 1]),
                    TestMp4.Box("stsz", new byte[4], [0, 0, 0, 1], huge),
                    TestMp4.Box("stco", new byte[4], [0, 0, 0, 1], [0, 0, 0, 0]))))))];

        Assert.Throws<InvalidDataException>(() => Mp4H264Reader.ToAnnexB(file));
    }

    [Fact]
    public void SamplesThatRepeatTheSameBytesAreRejected()
    {
        // 20 samples of 1000 bytes, all at the same place of a file of ~1.3 kB: the stream would repeat its bytes.
        const int Samples = 20;
        byte[] sample = [0, 0, 0x03, 0xE4, 0x65, .. new byte[995]];
        byte[] ftyp = TestMp4.Box("ftyp", "isom"u8.ToArray(), new byte[4], "isom"u8.ToArray());
        byte[] avc1 = TestMp4.Box("avc1", new byte[78], TestMp4.Box("avcC", [1, 0x42, 0, 0x1E, 0xFF, 0xE0, 0]));
        byte[] stsd = TestMp4.Box("stsd", new byte[4], [0, 0, 0, 1], avc1);
        byte[] tkhd = TestMp4.Box("tkhd", new byte[4], new byte[8], [0, 0, 0, 1], new byte[68]);
        byte[] Moov(uint mdat) => TestMp4.Box("moov", TestMp4.Box("trak", tkhd, TestMp4.Box("mdia", TestMp4.Box("minf", TestMp4.Box("stbl",
            stsd,
            TestMp4.Box("stsc", new byte[4], [0, 0, 0, 1], [0, 0, 0, 1], [0, 0, 0, 1], [0, 0, 0, 1]),
            TestMp4.Box("stsz", new byte[4], new byte[4], Be(Samples), [.. Enumerable.Repeat(Be((uint)sample.Length), Samples).SelectMany(static b => b)]),
            TestMp4.Box("stco", new byte[4], Be(Samples), [.. Enumerable.Repeat(Be(mdat + 8), Samples).SelectMany(static b => b)]))))));
        uint mdatStart = (uint)(ftyp.Length + Moov(0).Length);
        byte[] file = [.. ftyp, .. Moov(mdatStart), .. TestMp4.Box("mdat", sample)];

        Assert.Throws<InvalidDataException>(() => Mp4H264Reader.ToAnnexB(file));
    }

    private static byte[] Be(uint value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
}

/// <summary>Minimal MP4 files with one H.264 track, written from a clip (only the boxes the reader needs).</summary>
internal static class TestMp4
{
    public static byte[] FromClip(H264Clip clip, bool fragmented)
    {
        byte[][] samples = [.. clip.Frames.Select(static frame => frame.SelectMany(static nal => (byte[])[.. BigEndian((uint)nal.Length), .. nal]).ToArray())];
        byte[] sps = clip.Frames[clip.LoopStart].First(static n => (n[0] & 0x1F) == 7);
        byte[] pps = clip.Frames[clip.LoopStart].First(static n => (n[0] & 0x1F) == 8);
        byte[] avcC = [1, sps[1], sps[2], sps[3], 0xFF, 0xE1, .. BigEndian16(sps.Length), .. sps, 1, .. BigEndian16(pps.Length), .. pps];
        byte[] avc1 = Box("avc1", new byte[78], Box("avcC", avcC));
        byte[] stsd = Box("stsd", FullBox(), BigEndian(1), avc1);
        byte[] ftyp = Box("ftyp", "isom"u8.ToArray(), new byte[4], "isomavc1"u8.ToArray());
        byte[] tkhd = Box("tkhd", FullBox(), new byte[8], BigEndian(1), new byte[68]);
        if (!fragmented)
        {
            // Samples in two chunks of different size, to read the sample-to-chunk table properly.
            int firstChunk = samples.Length / 2;
            byte[] stsc = Box("stsc", FullBox(), BigEndian(2), BigEndian(1), BigEndian((uint)firstChunk), BigEndian(1), BigEndian(2), BigEndian((uint)(samples.Length - firstChunk)), BigEndian(1));
            byte[] stsz = Box("stsz", FullBox(), BigEndian(0), BigEndian((uint)samples.Length), [.. samples.SelectMany(static s => BigEndian((uint)s.Length))]);
            byte[] Moov(uint mdatStart) => Box("moov", Box("trak", tkhd, Box("mdia", Box("minf", Box("stbl", stsd, stsc, stsz,
                Box("stco", FullBox(), BigEndian(2), BigEndian(mdatStart + 8), BigEndian(mdatStart + 8 + (uint)samples.Take(firstChunk).Sum(static s => s.Length))))))));
            uint start = (uint)(ftyp.Length + Moov(0).Length);
            return [.. ftyp, .. Moov(start), .. Box("mdat", [.. samples.SelectMany(static s => s)])];
        }

        byte[] emptyStbl = Box("stbl", stsd, Box("stsc", FullBox(), BigEndian(0)), Box("stsz", FullBox(), BigEndian(0), BigEndian(0)), Box("stco", FullBox(), BigEndian(0)));
        byte[] trex = Box("trex", FullBox(), BigEndian(1), BigEndian(1), BigEndian(3600), BigEndian(0), BigEndian(0));
        List<byte> file = [.. ftyp, .. Box("moov", Box("trak", tkhd, Box("mdia", Box("minf", emptyStbl))), Box("mvex", trex))];
        // One fragment per key frame, like FFmpeg's frag_keyframe (default-base-is-moof, sizes in the run).
        for (int start = 0; start < samples.Length;)
        {
            int end = start + 1;
            while (end < samples.Length && !clip.Frames[end].Any(static n => (n[0] & 0x1F) == 5))
            {
                end++;
            }

            byte[][] part = samples[start..end];
            byte[] Moof(int dataOffset) => Box("moof", Box("mfhd", FullBox(), BigEndian((uint)start)), Box("traf",
                Box("tfhd", FullBox(0x020000), BigEndian(1)),
                Box("trun", FullBox(0x000201), BigEndian((uint)part.Length), BigEndian((uint)dataOffset), [.. part.SelectMany(static s => BigEndian((uint)s.Length))])));
            int moofLength = Moof(0).Length;
            file.AddRange(Moof(moofLength + 8));
            file.AddRange(Box("mdat", [.. part.SelectMany(static s => s)]));
            start = end;
        }

        return [.. file];
    }

    public static byte[] Box(string type, params byte[][] parts)
    {
        int length = 8 + parts.Sum(static p => p.Length);
        return [.. BigEndian((uint)length), .. System.Text.Encoding.ASCII.GetBytes(type), .. parts.SelectMany(static p => p)];
    }

    private static byte[] FullBox(uint flags = 0) => BigEndian(flags);

    private static byte[] BigEndian(uint value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] BigEndian16(int value) => [(byte)(value >> 8), (byte)value];
}
