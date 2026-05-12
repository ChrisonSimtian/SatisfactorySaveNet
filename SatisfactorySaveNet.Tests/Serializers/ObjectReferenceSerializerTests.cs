using FluentAssertions;
using static SatisfactorySaveNet.Tests.Serializers.BinaryReaderHelpers;

namespace SatisfactorySaveNet.Tests.Serializers;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public class ObjectReferenceSerializerTests
{
    [Test]
    public void Deserialize_TwoFStrings_LevelThenPath()
    {
        using var reader = MakeReader(w =>
            w.WriteObjectReference("Persistent_Level", "/Game/FactoryGame/Buildable/Foo.Foo_C"));

        var result = ObjectReferenceSerializer.Instance.Deserialize(reader);

        result.LevelName.Should().Be("Persistent_Level");
        result.PathName.Should().Be("/Game/FactoryGame/Buildable/Foo.Foo_C");
        reader.BaseStream.Position.Should().Be(reader.BaseStream.Length);
    }

    [Test]
    public void Deserialize_BothEmpty_ReturnsEmptyReference()
    {
        using var reader = MakeReader(w => w.WriteObjectReference("", ""));

        var result = ObjectReferenceSerializer.Instance.Deserialize(reader);

        result.LevelName.Should().BeEmpty();
        result.PathName.Should().BeEmpty();
    }
}
