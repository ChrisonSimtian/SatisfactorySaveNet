using FluentAssertions;
using static SatisfactorySaveNet.Tests.Serializers.BinaryReaderHelpers;

namespace SatisfactorySaveNet.Tests.Serializers;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public class SoftObjectReferenceSerializerTests
{
    [Test]
    public void Deserialize_ThreeFStrings_InOrder()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Persistent_Level");
            w.WriteFString("/Game/FactoryGame/Buildable/Foo.Foo_C");
            w.WriteFString("ExtraTag");
        });

        var result = SoftObjectReferenceSerializer.Instance.Deserialize(reader);

        result.LevelName.Should().Be("Persistent_Level");
        result.PathName.Should().Be("/Game/FactoryGame/Buildable/Foo.Foo_C");
        result.Unknown1.Should().Be("ExtraTag");
        reader.BaseStream.Position.Should().Be(reader.BaseStream.Length);
    }

    [Test]
    public void Deserialize_EmptyUnknown1_StillSucceeds()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Level");
            w.WriteFString("Path");
            w.WriteFString("");
        });

        var result = SoftObjectReferenceSerializer.Instance.Deserialize(reader);

        result.Unknown1.Should().BeEmpty();
    }
}
