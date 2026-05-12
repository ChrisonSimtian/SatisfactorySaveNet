using FluentAssertions;
using static SatisfactorySaveNet.Tests.Serializers.BinaryReaderHelpers;

namespace SatisfactorySaveNet.Tests.Serializers;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public class HexSerializerTests
{
    [Test]
    public void Deserialize_ReadsRequestedByteCount_AsAsciiChars()
    {
        using var reader = MakeReader(w => w.Write(new byte[] { 0x41, 0x42, 0x43, 0x44 })); // 'A','B','C','D'

        var result = HexSerializer.Instance.Deserialize(reader, 4);

        result.Should().Be("ABCD");
        reader.BaseStream.Position.Should().Be(4);
    }

    [Test]
    public void Deserialize_StopsAtRequestedLength_LeavesRemainder()
    {
        using var reader = MakeReader(w => w.Write(new byte[] { 0xAB, 0xCD, 0xEF, 0x99, 0x88 }));

        var result = HexSerializer.Instance.Deserialize(reader, 2);

        result.Should().HaveLength(2);
        reader.BaseStream.Position.Should().Be(2, "the serializer must consume exactly `length` bytes");
    }
}
