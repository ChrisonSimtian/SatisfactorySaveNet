using FluentAssertions;
using static SatisfactorySaveNet.Tests.Serializers.BinaryReaderHelpers;

namespace SatisfactorySaveNet.Tests.Serializers;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public class StringSerializerTests
{
    [Test]
    public void Deserialize_PositiveCount_ReadsUtf8Bytes()
    {
        using var reader = MakeReader(w => w.WriteFString("Hello"));

        var result = StringSerializer.Instance.Deserialize(reader);

        result.Should().Be("Hello");
        reader.BaseStream.Position.Should().Be(reader.BaseStream.Length);
    }

    [Test]
    public void Deserialize_ZeroCount_ReturnsEmpty()
    {
        using var reader = MakeReader(w => w.WriteFString(""));

        var result = StringSerializer.Instance.Deserialize(reader);

        result.Should().BeEmpty();
    }

    [Test]
    public void Deserialize_NegativeCount_ReadsUtf16Bytes()
    {
        // Names like "Über" need the Unicode branch — count is negated character
        // count INCLUDING the null terminator and each char takes 2 bytes.
        using var reader = MakeReader(w => w.WriteFStringUnicode("Über"));

        var result = StringSerializer.Instance.Deserialize(reader);

        result.Should().Be("Über");
        reader.BaseStream.Position.Should().Be(reader.BaseStream.Length);
    }
}
