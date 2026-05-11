using FluentAssertions;
using SatisfactorySaveNet.Abstracts.Model;
using static SatisfactorySaveNet.Tests.Serializers.BinaryReaderHelpers;

namespace SatisfactorySaveNet.Tests.Serializers;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public class ChunkSerializerTests
{
    [Test]
    public void Deserialize_ReadsFourInt32_InOrder()
    {
        using var reader = MakeReader(w =>
        {
            w.Write(ChunkInfo.MagicValue);   // CompressedSize (used as magic for the leading chunk)
            w.Write(0);                       // CompressedOffset
            w.Write(ChunkInfo.ChunkSize);    // UncompressedSize
            w.Write(0);                       // UncompressedOffset
        });

        var chunk = ChunkSerializer.Instance.Deserialize(reader);

        chunk.CompressedSize.Should().Be(ChunkInfo.MagicValue);
        chunk.CompressedOffset.Should().Be(0);
        chunk.UncompressedSize.Should().Be(ChunkInfo.ChunkSize);
        chunk.UncompressedOffset.Should().Be(0);
        reader.BaseStream.Position.Should().Be(16);
    }

    [Test]
    public void Deserialize_NonHeaderChunk_ReadsActualSizes()
    {
        using var reader = MakeReader(w =>
        {
            w.Write(1024);   // CompressedSize — payload chunk
            w.Write(64);     // CompressedOffset
            w.Write(8192);   // UncompressedSize
            w.Write(128);    // UncompressedOffset
        });

        var chunk = ChunkSerializer.Instance.Deserialize(reader);

        chunk.CompressedSize.Should().Be(1024);
        chunk.CompressedOffset.Should().Be(64);
        chunk.UncompressedSize.Should().Be(8192);
        chunk.UncompressedOffset.Should().Be(128);
    }
}
