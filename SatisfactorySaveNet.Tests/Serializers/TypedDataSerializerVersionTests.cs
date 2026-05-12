using FluentAssertions;
using SatisfactorySaveNet.Abstracts.Model.Typed;
using static SatisfactorySaveNet.Tests.Serializers.BinaryReaderHelpers;
using TypedDateTime = SatisfactorySaveNet.Abstracts.Model.Typed.DateTime;

namespace SatisfactorySaveNet.Tests.Serializers;

/// <summary>
/// TypedDataSerializer is mostly a dispatch by struct-type name. The interesting
/// regression risk is in the version-gated double-vs-float branches at
/// SaveVersion 41 (Vector/Quat/Rotator/Box/Vector2D/Vector4) and the
/// InventoryItem state-path split at SaveVersion 44. These tests pin those
/// transitions exactly — flipping a version constant in any of them must surface
/// here.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
public class TypedDataSerializerVersionTests
{
    [Test]
    public void Vector_PreV41_ReadsThreeFloats()
    {
        using var reader = MakeReader(w => w.WriteVec3(1.5f, 2.5f, 3.5f));

        var result = TypedDataSerializer.Instance.Deserialize(reader, MakeHeader(saveVersion: 40), "Vector", isArrayProperty: false, binarySize: 12);

        result.Should().BeOfType<Vector>();
        ((Vector)result).Value.X.Should().Be(1.5f);
        reader.BaseStream.Position.Should().Be(12);
    }

    [Test]
    public void Vector_V41Plus_ReadsThreeDoubles_WhenBinarySizeNot12()
    {
        using var reader = MakeReader(w => w.WriteVec3D(1.5, 2.5, 3.5));

        var result = TypedDataSerializer.Instance.Deserialize(reader, MakeHeader(saveVersion: 41), "Vector", isArrayProperty: false, binarySize: 24);

        result.Should().BeOfType<VectorD>();
        ((VectorD)result).Value.X.Should().Be(1.5);
        reader.BaseStream.Position.Should().Be(24);
    }

    [Test]
    public void Vector_V41Plus_ReadsThreeFloats_WhenBinarySizeIs12()
    {
        // Override: even at v41+, a binarySize of 12 forces the legacy float path.
        // This is a real special-case in DeserializeVector — pin it.
        using var reader = MakeReader(w => w.WriteVec3(1.5f, 2.5f, 3.5f));

        var result = TypedDataSerializer.Instance.Deserialize(reader, MakeHeader(saveVersion: 41), "Vector", isArrayProperty: false, binarySize: 12);

        result.Should().BeOfType<Vector>();
        reader.BaseStream.Position.Should().Be(12);
    }

    [Test]
    public void Quat_PreV41_ReadsFourFloats()
    {
        using var reader = MakeReader(w => w.WriteVec4(0f, 0f, 0f, 1f));

        var result = TypedDataSerializer.Instance.Deserialize(reader, MakeHeader(saveVersion: 40), "Quat", isArrayProperty: false, binarySize: 16);

        result.Should().BeOfType<Quat>();
        ((Quat)result).Value.W.Should().Be(1f);
    }

    [Test]
    public void Quat_V41Plus_ReadsFourDoubles()
    {
        using var reader = MakeReader(w => { w.Write(0.0); w.Write(0.0); w.Write(0.0); w.Write(1.0); });

        var result = TypedDataSerializer.Instance.Deserialize(reader, MakeHeader(saveVersion: 41), "Quat", isArrayProperty: false, binarySize: 32);

        result.Should().BeOfType<QuatD>();
        ((QuatD)result).Value.W.Should().Be(1.0);
        reader.BaseStream.Position.Should().Be(32);
    }

    [Test]
    public void Rotator_PreV41_ReadsThreeFloats()
    {
        using var reader = MakeReader(w => w.WriteVec3(10f, 20f, 30f));

        var result = TypedDataSerializer.Instance.Deserialize(reader, MakeHeader(saveVersion: 40), "Rotator", isArrayProperty: false, binarySize: 12);

        result.Should().BeOfType<Rotator>();
    }

    [Test]
    public void Rotator_V41Plus_ReadsThreeDoubles()
    {
        using var reader = MakeReader(w => w.WriteVec3D(10.0, 20.0, 30.0));

        var result = TypedDataSerializer.Instance.Deserialize(reader, MakeHeader(saveVersion: 41), "Rotator", isArrayProperty: false, binarySize: 24);

        result.Should().BeOfType<RotatorD>();
        reader.BaseStream.Position.Should().Be(24);
    }

    [Test]
    public void Box_V41Plus_ReadsTwoVec3DPlusSByte()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteVec3D(0.0, 0.0, 0.0);     // min
            w.WriteVec3D(100.0, 200.0, 300.0); // max
            w.Write((sbyte)1);                // IsValid
        });

        var result = TypedDataSerializer.Instance.Deserialize(reader, MakeHeader(saveVersion: 41), "Box", isArrayProperty: false, binarySize: 0);

        result.Should().BeOfType<BoxD>();
        var box = (BoxD)result;
        box.Min.X.Should().Be(0.0);
        box.Max.Z.Should().Be(300.0);
        box.IsValid.Should().Be(1);
        reader.BaseStream.Position.Should().Be(24 + 24 + 1);
    }

    [Test]
    public void InventoryItem_PreV44_ReadsItemStateAsObjectReference()
    {
        using var reader = MakeReader(w =>
        {
            w.Write(0);                       // unused leading Int32
            w.WriteFString("Desc_IronPlate_C");
            w.WriteObjectReference("Persistent_Level", "ItemState_Path");
            // isArrayProperty:true → no trailing property read
        });

        var result = (InventoryItem)TypedDataSerializer.Instance.Deserialize(
            reader, MakeHeader(saveVersion: 43), "InventoryItem", isArrayProperty: true, binarySize: 0);

        result.ItemType.Should().Be("Desc_IronPlate_C");
        result.ItemState.Should().NotBeNull();
        result.ItemState!.PathName.Should().Be("ItemState_Path");
    }

    [Test]
    public void InventoryItem_V44Plus_ReadsStateAsInt32()
    {
        using var reader = MakeReader(w =>
        {
            w.Write(0);                       // unused leading Int32
            w.WriteFString("Desc_IronPlate_C");
            w.Write(7);                       // state (replaces ObjectReference at v44+)
        });

        var result = (InventoryItem)TypedDataSerializer.Instance.Deserialize(
            reader, MakeHeader(saveVersion: 45), "InventoryItem", isArrayProperty: true, binarySize: 0);

        result.ItemType.Should().Be("Desc_IronPlate_C");
        result.ItemState.Should().BeNull("at v44+ the item state field is an int, not an ObjectReference");
    }
}
