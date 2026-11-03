using System.Buffers.Binary;
namespace VoiceLite.Core;

public sealed record WaveInfo(int SampleRate, int Channels, int BitsPerSample, long Frames)
{
    public double DurationSeconds => (double)Frames / SampleRate;
    public static WaveInfo Inspect(ReadOnlySpan<byte> data)
    {
        static VoiceLiteException Invalid() => new("音频不是完整的 16 位 PCM WAV，请重新生成或选择有效录音。");
        if (data.Length < 44 || !data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("WAVE"u8)) throw Invalid();
        long end = 8L + BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4, 4));
        if (end > data.Length || end < 44) throw Invalid();
        int rate = 0, channels = 0, bits = 0, align = 0;
        uint audioBytes = 0;
        bool foundFormat = false, foundData = false;
        for (long position = 12; position < end;)
        {
            if (position + 8 > end) throw Invalid();
            int start = (int)position;
            var tag = data.Slice(start, 4);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(start + 4, 4));
            long next = position + 8 + size;
            if (next > end) throw Invalid();
            if (tag.SequenceEqual("fmt "u8))
            {
                if (foundFormat || size < 16) throw Invalid();
                var fmt = data.Slice(start + 8, 16);
                if (BinaryPrimitives.ReadUInt16LittleEndian(fmt) != 1) throw Invalid();
                channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]);
                rate = BinaryPrimitives.ReadInt32LittleEndian(fmt[4..]);
                var byteRate = BinaryPrimitives.ReadInt32LittleEndian(fmt[8..]);
                align = BinaryPrimitives.ReadUInt16LittleEndian(fmt[12..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]);
                if (channels is < 1 or > 2 || bits != 16 || rate is < 8000 or > 192000 || align != channels * 2 || byteRate != rate * align) throw Invalid();
                foundFormat = true;
            }
            else if (tag.SequenceEqual("data"u8))
            {
                if (foundData || size == 0) throw Invalid();
                foundData = true; audioBytes = size;
            }
            position = next + (size & 1);
            if (position > end) throw Invalid();
        }
        if (!foundFormat || !foundData || audioBytes % align != 0) throw Invalid();
        return new(rate, channels, bits, audioBytes / align);
    }
}
