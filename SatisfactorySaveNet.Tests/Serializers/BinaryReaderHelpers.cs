using SatisfactorySaveNet.Abstracts.Model;
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

    /// <summary>
    /// Writes an Unreal FString in UTF-16 (Unicode) form — the negative-count branch
    /// the StringSerializer takes when the high bit is set: count is the negated
    /// character count INCLUDING the null terminator; bytes are 2 × that.
    /// </summary>
    public static void WriteFStringUnicode(this BinaryWriter writer, string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value + "\0");
        writer.Write(-(value.Length + 1));
        writer.Write(bytes);
    }

    public static void WriteGuid(this BinaryWriter writer, Guid guid)
    {
        writer.Write(guid.ToByteArray());
    }

    /// <summary>Writes a length-prefixed (levelName, pathName) pair the way ObjectReferenceSerializer reads it.</summary>
    public static void WriteObjectReference(this BinaryWriter writer, string levelName, string pathName)
    {
        writer.WriteFString(levelName);
        writer.WriteFString(pathName);
    }

    public static void WriteVec3(this BinaryWriter writer, float x, float y, float z)
    {
        writer.Write(x); writer.Write(y); writer.Write(z);
    }

    public static void WriteVec3D(this BinaryWriter writer, double x, double y, double z)
    {
        writer.Write(x); writer.Write(y); writer.Write(z);
    }

    public static void WriteVec4(this BinaryWriter writer, float x, float y, float z, float w)
    {
        writer.Write(x); writer.Write(y); writer.Write(z); writer.Write(w);
    }

    /// <summary>
    /// Builds a Header POCO with sensible v1.2-style defaults. Pass overrides via the
    /// <paramref name="configure"/> lambda — useful for tests that need a specific
    /// SaveVersion / HeaderVersion / BuildVersion combination to flip a version gate.
    /// </summary>
    public static Header MakeHeader(int saveVersion = 60, int headerVersion = 14, int buildVersion = 489969, Action<Header>? configure = null)
    {
        var header = new Header
        {
            HeaderVersion = headerVersion,
            SaveVersion = saveVersion,
            BuildVersion = buildVersion,
            MapName = "Persistent_Level",
            MapOptions = string.Empty,
            SessionName = "TestSession",
            PlayedSeconds = 0,
            SaveDateTimeUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            SessionVisibility = 0,
            EditorObjectVersion = 0,
            ModMetadata = string.Empty,
            IsModdedSave = 0,
            SaveIdentifier = string.Empty,
            IsPartitionedWorld = 0,
            SaveDataHash = string.Empty,
            IsCreativeModeEnabled = 0
        };
        configure?.Invoke(header);
        return header;
    }
}
