namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>
/// A minimal H.264 encoder for tests (T-155): every frame is an IDR picture of uncompressed I_PCM macroblocks
/// (Constrained Baseline, CAVLC), so any grayscale picture can be streamed losslessly without an encoder library. The
/// output is an Annex B stream for <c>H264Clip</c> (the simulator's <c>--camera-clip</c>); every frame carries SPS and PPS
/// like the Buddy3D's key frames. Large (1.5 bytes per pixel), so meant for small frames.
/// </summary>
internal static class PcmH264Writer
{
    /// <param name="frames">8-bit luma, rows top-down, <paramref name="width"/> × <paramref name="height"/> each.</param>
    /// <param name="width">A multiple of 16.</param>
    /// <param name="height">Even; padded to whole macroblocks and cropped again in the SPS.</param>
    public static byte[] Encode(IEnumerable<byte[]> frames, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(width % 16, 0);
        ArgumentOutOfRangeException.ThrowIfNotEqual(height % 2, 0);
        int mbWidth = width / 16;
        int mbHeight = (height + 15) / 16;
        byte[] sps = Nal(0x67, SequenceParameterSet(mbWidth, mbHeight, (mbHeight * 16) - height));
        byte[] pps = Nal(0x68, PictureParameterSet());
        using MemoryStream stream = new();
        int index = 0;
        foreach (byte[] frame in frames)
        {
            ArgumentOutOfRangeException.ThrowIfNotEqual(frame.Length, width * height);
            stream.Write(sps);
            stream.Write(pps);
            stream.Write(Nal(0x65, Slice(frame, width, height, mbWidth, mbHeight, idrPicId: index % 2)));
            index++;
        }

        return stream.ToArray();
    }

    private static byte[] SequenceParameterSet(int mbWidth, int mbHeight, int cropRows)
    {
        BitWriter bits = new();
        bits.Write(66, 8); // profile_idc: Baseline
        bits.Write(0xC0, 8); // constraint_set0 and 1: Constrained Baseline
        bits.Write(40, 8); // level_idc 4.0
        bits.WriteUe(0); // seq_parameter_set_id
        bits.WriteUe(0); // log2_max_frame_num_minus4
        bits.WriteUe(2); // pic_order_cnt_type
        bits.WriteUe(1); // max_num_ref_frames
        bits.Write(0, 1); // gaps_in_frame_num_value_allowed_flag
        bits.WriteUe(mbWidth - 1);
        bits.WriteUe(mbHeight - 1);
        bits.Write(1, 1); // frame_mbs_only_flag
        bits.Write(1, 1); // direct_8x8_inference_flag
        bits.Write(cropRows > 0 ? 1 : 0, 1); // frame_cropping_flag
        if (cropRows > 0)
        {
            bits.WriteUe(0);
            bits.WriteUe(0);
            bits.WriteUe(0);
            bits.WriteUe(cropRows / 2); // in units of two rows (4:2:0)
        }

        bits.Write(0, 1); // vui_parameters_present_flag
        bits.WriteTrailingBits();
        return bits.ToArray();
    }

    private static byte[] PictureParameterSet()
    {
        BitWriter bits = new();
        bits.WriteUe(0); // pic_parameter_set_id
        bits.WriteUe(0); // seq_parameter_set_id
        bits.Write(0, 1); // entropy_coding_mode_flag: CAVLC
        bits.Write(0, 1); // bottom_field_pic_order_in_frame_present_flag
        bits.WriteUe(0); // num_slice_groups_minus1
        bits.WriteUe(0); // num_ref_idx_l0_default_active_minus1
        bits.WriteUe(0); // num_ref_idx_l1_default_active_minus1
        bits.Write(0, 1); // weighted_pred_flag
        bits.Write(0, 2); // weighted_bipred_idc
        bits.WriteSe(0); // pic_init_qp_minus26
        bits.WriteSe(0); // pic_init_qs_minus26
        bits.WriteSe(0); // chroma_qp_index_offset
        bits.Write(1, 1); // deblocking_filter_control_present_flag
        bits.Write(0, 1); // constrained_intra_pred_flag
        bits.Write(0, 1); // redundant_pic_cnt_present_flag
        bits.WriteTrailingBits();
        return bits.ToArray();
    }

    private static byte[] Slice(byte[] frame, int width, int height, int mbWidth, int mbHeight, int idrPicId)
    {
        BitWriter bits = new();
        bits.WriteUe(0); // first_mb_in_slice
        bits.WriteUe(7); // slice_type: I (all slices)
        bits.WriteUe(0); // pic_parameter_set_id
        bits.Write(0, 4); // frame_num (log2_max_frame_num = 4)
        bits.WriteUe(idrPicId); // idr_pic_id: differs between consecutive IDR pictures
        bits.Write(0, 1); // no_output_of_prior_pics_flag
        bits.Write(0, 1); // long_term_reference_flag
        bits.WriteSe(0); // slice_qp_delta
        bits.WriteUe(1); // disable_deblocking_filter_idc: off (the samples are exact)
        for (int my = 0; my < mbHeight; my++)
        {
            for (int mx = 0; mx < mbWidth; mx++)
            {
                bits.WriteUe(25); // mb_type: I_PCM
                bits.AlignWithZeros(); // pcm_alignment_zero_bit
                for (int y = 0; y < 16; y++)
                {
                    int row = Math.Min((my * 16) + y, height - 1);
                    for (int x = 0; x < 16; x++)
                    {
                        // Full-range gray to video-range luma (16–235), as the decoder converts it back.
                        bits.WriteByte((byte)(16 + ((frame[(row * width) + (mx * 16) + x] * 219) + 127) / 255));
                    }
                }

                for (int i = 0; i < 2 * 64; i++)
                {
                    bits.WriteByte(128); // Cb and Cr: no colour
                }
            }
        }

        bits.WriteTrailingBits();
        return bits.ToArray();
    }

    /// <summary>A NAL unit with its start code; emulation prevention bytes inserted into the payload.</summary>
    private static byte[] Nal(byte header, byte[] rbsp)
    {
        List<byte> nal = new(rbsp.Length + (rbsp.Length / 64) + 5) { 0, 0, 0, 1, header };
        int zeros = 0;
        foreach (byte value in rbsp)
        {
            if (zeros == 2 && value <= 3)
            {
                nal.Add(3);
                zeros = 0;
            }

            nal.Add(value);
            zeros = value == 0 ? zeros + 1 : 0;
        }

        return [.. nal];
    }

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = [];
        private int _current;
        private int _count;

        public void Write(int value, int bits)
        {
            for (int i = bits - 1; i >= 0; i--)
            {
                _current = (_current << 1) | ((value >> i) & 1);
                if (++_count == 8)
                {
                    _bytes.Add((byte)_current);
                    _current = 0;
                    _count = 0;
                }
            }
        }

        public void WriteByte(byte value)
        {
            if (_count == 0)
            {
                _bytes.Add(value);
                return;
            }

            Write(value, 8);
        }

        public void WriteUe(int value)
        {
            int code = value + 1;
            int length = 32 - int.LeadingZeroCount(code);
            Write(0, length - 1);
            Write(code, length);
        }

        public void WriteSe(int value) => WriteUe(value > 0 ? (2 * value) - 1 : -2 * value);

        public void AlignWithZeros()
        {
            if (_count > 0)
            {
                Write(0, 8 - _count);
            }
        }

        public void WriteTrailingBits()
        {
            Write(1, 1);
            AlignWithZeros();
        }

        public byte[] ToArray() => [.. _bytes];
    }
}
