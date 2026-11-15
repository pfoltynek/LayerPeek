namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>T-207: the SPS and PPS a decoder remembers from the packets of a stream without them in its description.</summary>
public sealed class H264ParameterSetMemoryTests
{
    private static readonly byte[] Sps = [0x67, 0x42, 0xC0, 0x1F, 0x8C, 0x8D];
    private static readonly byte[] Pps = [0x68, 0xCE, 0x3C, 0x80];

    private static byte[] Packet(params byte[][] nals)
    {
        List<byte> data = [];
        foreach (byte[] nal in nals)
        {
            data.AddRange([0, 0, 0, 1]);
            data.AddRange(nal);
        }

        return [.. data];
    }

    private static byte[] AnnexB(H264ParameterSetMemory sets)
    {
        byte[] data = new byte[sets.AnnexBLength];
        sets.WriteAnnexB(data);
        return data;
    }

    [Fact]
    public void RemembersTheSpsAndPpsInFrontOfAKeyFrame()
    {
        H264ParameterSetMemory sets = new();

        // Access unit delimiter and SEI before them, three-byte start codes and trailing zeros as FFmpeg may hand them over.
        sets.Observe([0, 0, 1, 0x09, 0xF0, 0, 0, 0, 1, 0x06, 0x05, 0x01, .. Packet(Sps, Pps), 0, 0, 1, 0x65, 0x88, 0x84, 0, 0]);

        Assert.True(sets.IsComplete);
        Assert.Equal(Packet(Sps, Pps), AnnexB(sets));
    }

    [Fact]
    public void NothingIsCompleteWithoutBothSets()
    {
        H264ParameterSetMemory sets = new();
        sets.Observe(Packet(Sps, [0x65, 0x88]));

        Assert.False(sets.IsComplete);
        Assert.Equal(0, sets.AnnexBLength);
        Assert.Throws<InvalidOperationException>(() => sets.WriteAnnexB(new byte[64]));

        // The PPS may come in a packet of its own.
        sets.Observe(Packet(Pps));
        Assert.Equal(Packet(Sps, Pps), AnnexB(sets));
    }

    [Fact]
    public void LooksNoFurtherThanTheFirstSlice()
    {
        H264ParameterSetMemory sets = new();

        // A slice first: the packet is not looked into, nor after the slice of an IDR frame.
        sets.Observe(Packet([0x41, 0x9A], Sps, Pps));
        sets.Observe(Packet([0x65, 0x88], Sps, Pps));

        Assert.False(sets.IsComplete);
        Assert.Equal(0, sets.Changes);
    }

    [Fact]
    public void KeepsTheLatestSetsAndRewritesOnlyChangedOnes()
    {
        H264ParameterSetMemory sets = new();
        sets.Observe(Packet(Sps, Pps, [0x65, 0x88]));
        Assert.Equal(2, sets.Changes);

        // Every IDR frame repeats them (like the Buddy3D): nothing is rewritten.
        for (int i = 0; i < 10; i++)
        {
            sets.Observe(Packet(Sps, Pps, [0x65, 0x88]));
        }

        Assert.Equal(2, sets.Changes);

        // A new resolution: a new SPS, the PPS stays.
        byte[] newSps = [0x67, 0x64, 0x00, 0x28, 0xAC];
        sets.Observe(Packet(newSps, Pps, [0x65, 0x88]));
        Assert.Equal(3, sets.Changes);
        Assert.Equal(Packet(newSps, Pps), AnnexB(sets));
    }

    [Fact]
    public void ASetLargerThanTheLimitIsIgnoredAndThePreviousOneKept()
    {
        H264ParameterSetMemory sets = new();
        sets.Observe(Packet(Sps, Pps));

        byte[] largest = new byte[H264ParameterSetMemory.MaxSetSize];
        largest.AsSpan().Fill(0xFF);
        largest[0] = 0x67;
        byte[] tooLarge = [.. largest, 0xFF];

        sets.Observe(Packet(tooLarge));
        Assert.Equal(Packet(Sps, Pps), AnnexB(sets));

        // Exactly at the limit is still taken.
        sets.Observe(Packet(largest));
        Assert.Equal(Packet(largest, Pps), AnnexB(sets));
    }

    [Fact]
    public void BrokenDataIsHarmless()
    {
        H264ParameterSetMemory sets = new();

        sets.Observe([]);
        sets.Observe([0, 0, 1]);
        sets.Observe([0, 0, 0, 1, 0, 0, 1, 0, 0, 1]);
        sets.Observe([0x67, 0x42, 0x00]);
        // AVCC (length-prefixed) data has no start codes.
        sets.Observe([0, 0, 0, 4, .. Pps]);

        Assert.False(sets.IsComplete);
        Assert.Equal(0, sets.Changes);
    }

    [Fact]
    public void RepeatedSetsDoNotAllocate()
    {
        H264ParameterSetMemory sets = new();
        byte[] keyFrame = Packet(Sps, Pps, [0x65, 0x88, 0x84]);
        byte[] frame = Packet([0x41, 0x9A, 0x02]);
        sets.Observe(keyFrame);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            sets.Observe(keyFrame);
            sets.Observe(frame);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
