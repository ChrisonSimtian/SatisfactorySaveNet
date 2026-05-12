using FluentAssertions;
using SatisfactorySaveNet.Abstracts.Exceptions;
using SatisfactorySaveNet.Abstracts.Model;
using static SatisfactorySaveNet.Tests.Serializers.BinaryReaderHelpers;

namespace SatisfactorySaveNet.Tests.Serializers;

/// <summary>
/// ObjectHeaderSerializer has one version gate (saveVersion >= 51 reads the
/// UInt32 Flags field) and dispatches on the leading Int32 type tag. The four
/// happy paths plus the unknown-type guard cover its surface.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
public class ObjectHeaderSerializerTests
{
    [Test]
    public void Deserialize_ActorHeader_PreV51_NoFlags()
    {
        using var reader = MakeReader(w =>
        {
            w.Write(ActorObject.TypeID);
            w.WriteFString("/Game/FactoryGame/Buildable/Foo.Foo_C");
            w.WriteObjectReference("Persistent_Level", "Foo_2");
            // saveVersion = 50 → no Flags read
            w.Write(1);                                      // NeedTransform
            w.WriteVec4(0f, 0f, 0f, 1f);                     // Rotation (identity quat)
            w.WriteVec3(100f, 200f, 50f);                    // Position
            w.WriteVec3(1f, 1f, 1f);                         // Scale
            w.Write(1);                                      // PlacedInLevel
        });

        var obj = ObjectHeaderSerializer.Instance.Deserialize(reader, saveVersion: 50);

        obj.Should().BeOfType<ActorObject>();
        var actor = (ActorObject)obj;
        actor.TypePath.Should().Be("/Game/FactoryGame/Buildable/Foo.Foo_C");
        actor.ObjectReference.PathName.Should().Be("Foo_2");
        actor.Flags.Should().BeNull("the flags field is only read at saveVersion >= 51");
        actor.NeedTransform.Should().Be(1);
        actor.Position.X.Should().Be(100f);
        actor.Scale.Should().Be(new Abstracts.Maths.Vector.Vector3(1f, 1f, 1f));
        actor.PlacedInLevel.Should().Be(1);
    }

    [Test]
    public void Deserialize_ActorHeader_V51Plus_ReadsFlags()
    {
        using var reader = MakeReader(w =>
        {
            w.Write(ActorObject.TypeID);
            w.WriteFString("Foo_C");
            w.WriteObjectReference("Persistent_Level", "Foo_2");
            w.Write(0xCAFEBABEu);                            // Flags — only present at >= 51
            w.Write(0);
            w.WriteVec4(0f, 0f, 0f, 1f);
            w.WriteVec3(0f, 0f, 0f);
            w.WriteVec3(1f, 1f, 1f);
            w.Write(0);
        });

        var obj = ObjectHeaderSerializer.Instance.Deserialize(reader, saveVersion: 51);

        var actor = (ActorObject)obj;
        actor.Flags.Should().Be(0xCAFEBABEu);
    }

    [Test]
    public void Deserialize_ComponentHeader_PreV51_NoFlags()
    {
        using var reader = MakeReader(w =>
        {
            w.Write(ComponentObject.TypeID);
            w.WriteFString("/Game/FactoryGame/Component/Inventory.Inventory_C");
            w.WriteObjectReference("Persistent_Level", "Inventory_3");
            w.WriteFString("Foo_2");                         // ParentActorName
        });

        var obj = ObjectHeaderSerializer.Instance.Deserialize(reader, saveVersion: 50);

        obj.Should().BeOfType<ComponentObject>();
        obj.TypePath.Should().Be("/Game/FactoryGame/Component/Inventory.Inventory_C");
        obj.ParentActorName.Should().Be("Foo_2");
        obj.Flags.Should().BeNull();
    }

    [Test]
    public void Deserialize_ComponentHeader_V51Plus_ReadsFlags()
    {
        using var reader = MakeReader(w =>
        {
            w.Write(ComponentObject.TypeID);
            w.WriteFString("Inventory_C");
            w.WriteObjectReference("Persistent_Level", "Inventory_3");
            w.Write(0xDEADBEEFu);
            w.WriteFString("Foo_2");
        });

        var obj = ObjectHeaderSerializer.Instance.Deserialize(reader, saveVersion: 51);

        obj.Flags.Should().Be(0xDEADBEEFu);
    }

    [Test]
    public void Deserialize_UnknownTypeId_Throws()
    {
        using var reader = MakeReader(w => w.Write(99));

        var act = () => ObjectHeaderSerializer.Instance.Deserialize(reader, saveVersion: 60);

        act.Should().Throw<CorruptedSatisFactorySaveFileException>();
    }
}
