using System;
using System.IO;
using System.Text;

namespace SatisfactorySaveNet.Tests.Serializers;

/// <summary>
/// Builds <see cref="BinaryReader"/> instances over synthesised byte sequences so a
/// test can describe the wire format declaratively. <see cref="WriteFString"/> mirrors
/// the length-prefixed null-terminated layout that
/// <c>SatisfactorySaveNet.StringSerializer</c> reads.
/// </summary>
internal static class BinaryReaderHelpers
{
    public static BinaryReader MakeReader(Action<BinaryWriter> write)
    {
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        write(writer);
        writer.Flush();
        stream.Position = 0;
        return new BinaryReader(stream);
    }

    public static byte[] BuildBytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        write(writer);
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>
    /// Writes an Unreal FString: int32 count + count UTF-8 bytes including trailing null.
    /// Empty strings collapse to count=0 with no payload, matching production saves.
    /// </summary>
    public static void WriteFString(this BinaryWriter writer, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            writer.Write(0);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length + 1);
        writer.Write(bytes);
        writer.Write((byte)0);
    }

    public static void WriteGuid(this BinaryWriter writer, Guid guid)
    {
        writer.Write(guid.ToByteArray());
    }
}
