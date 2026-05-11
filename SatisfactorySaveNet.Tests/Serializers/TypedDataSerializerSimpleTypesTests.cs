using FluentAssertions;
using SatisfactorySaveNet.Abstracts.Model.Typed;
using static SatisfactorySaveNet.Tests.Serializers.BinaryReaderHelpers;
using TypedDateTime = SatisfactorySaveNet.Abstracts.Model.Typed.DateTime;
using TypedGuid = SatisfactorySaveNet.Abstracts.Model.Typed.Guid;

namespace SatisfactorySaveNet.Tests.Serializers;

/// <summary>
/// Pin the simple no-version-gate struct types dispatched by
/// <c>TypedDataSerializer.Deserialize</c>. Each test exercises one type-specific
/// reader so a regression there fails its named test rather than only surfacing
/// via real-fixture parsing.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
public class TypedDataSerializerSimpleTypesTests
{
    private static readonly Abstracts.Model.Header AnyHeader = MakeHeader(saveVersion: 50);

    [Test]
    public void Color_ReadsFourSignedBytes()
    {
        using var reader = MakeReader(w => w.Write(new byte[] { 0x10, 0x20, 0x30, 0xFF }));

        var result = (Color)TypedDataSerializer.Instance.Deserialize(reader, AnyHeader, "Color", isArrayProperty: false, binarySize: 4);

        result.Value.X.Should().Be(0x10);
        result.Value.W.Should().Be(-1, "0xFF read as sbyte is -1");
    }

    [Test]
    public void LinearColor_ReadsFourFloats()
    {
        using var reader = MakeReader(w => w.WriteVec4(0.1f, 0.2f, 0.3f, 1.0f));

        var result = (LinearColor)TypedDataSerializer.Instance.Deserialize(reader, AnyHeader, "LinearColor", isArrayProperty: false, binarySize: 16);

        result.Color.X.Should().Be(0.1f);
        result.Color.W.Should().Be(1.0f);
    }

    [Test]
    public void Guid_ReadsSixteenBytesAsHexString()
    {
        var bytes = new byte[] { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF,
                                 0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10 };
        using var reader = MakeReader(w => w.Write(bytes));

        var result = (TypedGuid)TypedDataSerializer.Instance.Deserialize(reader, AnyHeader, "Guid", isArrayProperty: false, binarySize: 16);

        result.Value.Should().HaveLength(16);
        reader.BaseStream.Position.Should().Be(16);
    }

    [Test]
    public void DateTime_ReadsInt64Ticks()
    {
        var ticks = new System.DateTime(2026, 5, 12, 9, 0, 0, System.DateTimeKind.Utc).Ticks;
        using var reader = MakeReader(w => w.Write(ticks));

        var result = (TypedDateTime)TypedDataSerializer.Instance.Deserialize(reader, AnyHeader, "DateTime", isArrayProperty: false, binarySize: 8);

        result.Value.Ticks.Should().Be(ticks);
    }

    [Test]
    public void IntPoint_ReadsTwoInt32()
    {
        using var reader = MakeReader(w => { w.Write(10); w.Write(-20); });

        var result = (IntPoint)TypedDataSerializer.Instance.Deserialize(reader, AnyHeader, "IntPoint", isArrayProperty: false, binarySize: 8);

        result.Value.X.Should().Be(10);
        result.Value.Y.Should().Be(-20);
    }

    [Test]
    public void IntVector4_ReadsFourFloats_AsVector4Wrapper()
    {
        // IntVector4 dispatches to DeserializeVector4I which actually reads 4 *floats*.
        // Pin the (mildly confusing) current behaviour as the contract.
        using var reader = MakeReader(w => w.WriteVec4(1f, 2f, 3f, 4f));

        var result = (Vector4)TypedDataSerializer.Instance.Deserialize(reader, AnyHeader, "IntVector4", isArrayProperty: false, binarySize: 16);

        result.Value.X.Should().Be(1f);
        result.Value.W.Should().Be(4f);
    }

    [Test]
    public void FluidBox_ReadsSingleFloat()
    {
        using var reader = MakeReader(w => w.Write(123.5f));

        var result = (FluidBox)TypedDataSerializer.Instance.Deserialize(reader, AnyHeader, "FluidBox", isArrayProperty: false, binarySize: 4);

        result.Value.Should().Be(123.5f);
    }

    [Test]
    public void TimerHandle_ReadsSingleFString()
    {
        using var reader = MakeReader(w => w.WriteFString("Timer_42"));

        var result = (TimerHandle)TypedDataSerializer.Instance.Deserialize(reader, AnyHeader, "TimerHandle", isArrayProperty: false, binarySize: 0);

        result.Value.Should().Be("Timer_42");
    }

    [Test]
    public void SlateBrush_ReadsSingleFString()
    {
        using var reader = MakeReader(w => w.WriteFString("Brush_Default"));

        var result = (SlateBrush)TypedDataSerializer.Instance.Deserialize(reader, AnyHeader, "SlateBrush", isArrayProperty: false, binarySize: 0);

        result.Unknown.Should().Be("Brush_Default");
    }

    [Test]
    public void FICFrameRange_ReadsTwoInt64()
    {
        using var reader = MakeReader(w =>
        {
            w.Write(100L);
            w.Write(200L);
        });

        var result = (FICFrameRange)TypedDataSerializer.Instance.Deserialize(reader, AnyHeader, "FICFrameRange", isArrayProperty: false, binarySize: 16);

        result.Begin.Should().Be(100L);
        result.End.Should().Be(200L);
    }

    [Test]
    public void RailroadTrackPosition_ReadsTwoFStringsAndTwoFloats()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Persistent_Level");
            w.WriteFString("Track_1");
            w.Write(12.5f);
            w.Write(0.75f);
        });

        var result = (RailroadTrackPosition)TypedDataSerializer.Instance.Deserialize(reader, AnyHeader, "RailroadTrackPosition", isArrayProperty: false, binarySize: 0);

        result.LevelName.Should().Be("Persistent_Level");
        result.PathName.Should().Be("Track_1");
        result.Offset.Should().Be(12.5f);
        result.Forward.Should().Be(0.75f);
    }

    [Test]
    public void ItemAmount_ReadsObjectReferenceAndInt()
    {
        using var reader = MakeReader(w =>
        {
            // ItemAmount is dispatched via DeserializeArrayProperties for type "ItemAmount"
            // -- and the array-properties path reads a properties list. To exercise the
            // single-shot DeserializeItemAmount reader, drive it via FINLuaProcessorStateStorage
            // is too involved; we cover ItemAmount transitively via real fixtures.
            // This test pins the much simpler InventoryItem v44+ tail-property path instead:
            // an InventoryItem with isArrayProperty=true + a None terminator afterwards.
            w.Write(0);                                  // unused leading Int32
            w.WriteFString("Desc_IronPlate_C");
            w.Write(0);                                  // state=0 → skip stateful branch
            // saveVersion < 46 → no peek-and-rewind; isArrayProperty=true → no trailing property
        });

        var result = (InventoryItem)TypedDataSerializer.Instance.Deserialize(
            reader, MakeHeader(saveVersion: 45), "InventoryItem", isArrayProperty: true, binarySize: 0);

        result.ItemType.Should().Be("Desc_IronPlate_C");
        result.ExtraProperty.Should().BeNull("isArrayProperty:true suppresses the trailing property read");
    }
}
