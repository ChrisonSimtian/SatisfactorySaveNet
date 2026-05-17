using FluentAssertions;
using SatisfactorySaveNet.Abstracts.Model.Properties;
using System;
using System.Linq;
using static SatisfactorySaveNet.Tests.Serializers.BinaryReaderHelpers;

namespace SatisfactorySaveNet.Tests.Serializers;

/// <summary>
/// Tests the v1.2-specific branches of PropertySerializer — the per-list
/// <c>serializationControl</c> byte, the complete-tag flow, the flag bits for
/// optional index / property GUID, the BoolProperty flag-bit encoding, and the
/// binary-size fence that re-aligns the stream when a value parse over- or
/// under-reads.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
public class PropertySerializerV12Tests
{
    private const int V12 = 53;
    private const int PreV12 = 52;

    [Test]
    public void DeserializeProperties_AtV12_TopLevel_ConsumesSerializationControlByte()
    {
        using var reader = MakeReader(w =>
        {
            w.Write((byte)0xAB);         // serializationControl byte
            w.WriteFString("None");      // immediate terminator
        });

        var properties = PropertySerializer.Instance
            .DeserializeProperties(reader, saveVersion: V12)
            .ToArray();

        properties.Should().BeEmpty();
        reader.BaseStream.Position.Should().Be(reader.BaseStream.Length,
            "the byte AND the None terminator must both be consumed");
    }

    [Test]
    public void DeserializeProperties_PreV12_DoesNotConsumeSerializationControlByte()
    {
        using var reader = MakeReader(w => w.WriteFString("None"));

        var properties = PropertySerializer.Instance
            .DeserializeProperties(reader, saveVersion: PreV12)
            .ToArray();

        properties.Should().BeEmpty();
        reader.BaseStream.Position.Should().Be(reader.BaseStream.Length);
    }

    [Test]
    public void DeserializeProperties_AtV12_WithNonNullType_DoesNotConsumeSerializationControlByte()
    {
        // Inside an array/set element parser, type is set and the per-list control
        // byte does NOT exist. Synthesise just enough to exit immediately so we can
        // observe whether the leading byte was read.
        using var reader = MakeReader(w => w.Write((byte)0xAB));

        // The iterator is lazy — we have to materialise at least one MoveNext to observe
        // the byte-read decision. Wrap in try/catch because the inner switch will throw
        // on the bogus type, but only AFTER the (non-)read of the control byte.
        try
        {
            _ = PropertySerializer.Instance
                .DeserializeProperties(reader, type: "BoolProperty", saveVersion: V12)
                .ToArray();
        }
        catch
        {
            // expected — synthesised stream isn't a real array element
        }

        reader.BaseStream.Position.Should().Be(1,
            "the type != null branch must skip the v1.2 control byte read; it then reads 1 byte for BoolProperty");
    }

    [Test]
    public void DeserializeProperty_AtV12_IntProperty_ParsesValueAndAlignsStream()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Money");     // property name
            w.WriteFString("IntProperty"); w.Write(0);  // tag node: name + 0 children
            w.Write(4);                  // binarySize
            w.Write((byte)0);            // flags
            w.Write(12_345);             // value
            w.Write(0xDEADBEEFu);        // sentinel — must NOT be read
        });

        var prop = (RawProperty)PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: V12)!;

        prop.Name.Should().Be("Money");
        prop.Type.Should().Be("IntProperty");
        prop.BinarySize.Should().Be(4);
        prop.Flags.Should().Be(0);
        prop.IntValue.Should().Be(12_345);
        prop.Index.Should().Be(0);
        prop.PropertyGuid.Should().BeNull();
        reader.ReadUInt32().Should().Be(0xDEADBEEFu, "the fence must leave the stream at posBeforeValue + binarySize");
    }

    [Test]
    public void DeserializeProperty_AtV12_Flag0x1_ReadsIndex()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Stack");
            w.WriteFString("IntProperty"); w.Write(0);
            w.Write(4);                  // binarySize
            w.Write((byte)0x01);         // flags: hasIndex
            w.Write(99);                 // index
            w.Write(7);                  // value
        });

        var prop = (RawProperty)PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: V12)!;

        prop.Index.Should().Be(99);
        prop.IntValue.Should().Be(7);
        prop.PropertyGuid.Should().BeNull();
    }

    [Test]
    public void DeserializeProperty_AtV12_Flag0x2_ReadsPropertyGuid()
    {
        var guid = Guid.NewGuid();
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Tagged");
            w.WriteFString("IntProperty"); w.Write(0);
            w.Write(4);
            w.Write((byte)0x02);         // flags: hasGuid
            w.WriteGuid(guid);
            w.Write(42);
        });

        var prop = (RawProperty)PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: V12)!;

        prop.PropertyGuid.Should().Be(guid);
        prop.Index.Should().Be(0, "0x1 was not set");
        prop.IntValue.Should().Be(42);
    }

    [Test]
    public void DeserializeProperty_AtV12_BothFlags_ReadsIndexThenGuid()
    {
        var guid = Guid.NewGuid();
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Both");
            w.WriteFString("IntProperty"); w.Write(0);
            w.Write(4);
            w.Write((byte)0x03);         // flags: hasIndex + hasGuid
            w.Write(11);                 // index FIRST
            w.WriteGuid(guid);           // then guid
            w.Write(123);
        });

        var prop = (RawProperty)PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: V12)!;

        prop.Index.Should().Be(11);
        prop.PropertyGuid.Should().Be(guid);
        prop.IntValue.Should().Be(123);
    }

    [TestCase((byte)0x10, true)]
    [TestCase((byte)0x00, false)]
    public void DeserializeProperty_AtV12_BoolProperty_ValueComesFromFlagBit(byte flags, bool expected)
    {
        // The BoolProperty branch in TryParseKnownValue is only entered when
        // binarySize > 0 (see PropertySerializer.cs:117). The value itself lives
        // in flag bit 0x10, not in value bytes, but we still need a positive
        // binarySize to take the parse path. Pad with one opaque byte that the
        // fence will skip.
        using var reader = MakeReader(w =>
        {
            w.WriteFString("IsActive");
            w.WriteFString("BoolProperty"); w.Write(0);
            w.Write(1);                  // binarySize = 1 → triggers TryParseKnownValue
            w.Write(flags);              // bit 0x10 encodes the boolean
            w.Write((byte)0xAA);         // opaque value byte; fence skips it
        });

        var prop = (RawProperty)PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: V12)!;

        prop.Type.Should().Be("BoolProperty");
        prop.BoolValue.Should().Be(expected);
    }

    [Test]
    public void DeserializeProperty_AtV12_BoolProperty_WithZeroBinarySize_LeavesBoolValueNull()
    {
        // Documents a latent issue: when a BoolProperty's binarySize is 0 (which the
        // tag-format comment suggests is the real layout — "no value bytes"), the
        // production code's `if (binarySize > 0)` gate prevents TryParseKnownValue
        // from running, so BoolValue is never populated from the flag bit. Keep this
        // test as a regression marker; tighten it to assert the populated value if
        // the gate ever moves before the BoolProperty case.
        using var reader = MakeReader(w =>
        {
            w.WriteFString("IsActive");
            w.WriteFString("BoolProperty"); w.Write(0);
            w.Write(0);                  // binarySize = 0
            w.Write((byte)0x10);         // flags with bool=true bit set
        });

        var prop = (RawProperty)PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: V12)!;

        prop.Type.Should().Be("BoolProperty");
        prop.Flags.Should().Be(0x10);
        prop.BoolValue.Should().BeNull("the TryParseKnownValue gate is bypassed when binarySize == 0");
    }

    [Test]
    public void DeserializeProperty_AtV12_StrProperty_ParsesStringValue()
    {
        using var stream = new System.IO.MemoryStream();
        using var writer = new System.IO.BinaryWriter(stream);
        writer.WriteFString("Label");
        writer.WriteFString("StrProperty"); writer.Write(0);
        var binarySizeOffset = stream.Position;
        writer.Write(0);                 // placeholder, fix up below
        writer.Write((byte)0);

        var valueStart = stream.Position;
        writer.WriteFString("Hello world");
        var binarySize = (int)(stream.Position - valueStart);

        stream.Position = binarySizeOffset;
        writer.Write(binarySize);
        stream.Position = 0;

        using var reader = new System.IO.BinaryReader(stream);
        var prop = (RawProperty)PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: V12)!;

        prop.Type.Should().Be("StrProperty");
        prop.StringValue.Should().Be("Hello world");
    }

    [Test]
    public void DeserializeProperty_AtV12_UnknownVariableShapeType_AlignsViaFence()
    {
        // SetProperty is a variable-shape type the v1.2 path does NOT deep-parse.
        // After the tag, the stream must advance exactly `binarySize` bytes via the fence.
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Items");
            w.WriteFString("SetProperty"); w.Write(0);
            w.Write(8);                  // binarySize
            w.Write((byte)0);
            w.Write(0xDEADBEEFu);        // opaque value bytes
            w.Write(0xCAFEBABEu);
            w.Write(0xABCD1234u);        // sentinel — must NOT be read
        });

        var prop = (RawProperty)PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: V12)!;

        prop.Type.Should().Be("SetProperty");
        prop.BinarySize.Should().Be(8);
        reader.ReadUInt32().Should().Be(0xABCD1234u,
            "the fence must skip exactly 8 opaque bytes for a non-deep-parsed type");
    }

    [Test]
    public void DeserializeProperty_AtV12_None_ReturnsNull()
    {
        using var reader = MakeReader(w => w.WriteFString("None"));

        var prop = PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: V12);

        prop.Should().BeNull();
    }

    [Test]
    public void DeserializeProperty_AtV12_ArrayOfStruct_ParsesElementsAsInnerPropertyLists()
    {
        // ArrayProperty<StructProperty> at v1.2: the inner-tag header that v1.1 carried
        // before the element loop is gone — the struct subtype lives in TypeNode.Children[0]
        // (e.g. "SplinePointData"). Value bytes are: int32 count, then `count` element
        // bodies each a v1.2 property-list terminated by "None". No per-element
        // serializationControl byte (those only prefix the outer object body).
        using var stream = new System.IO.MemoryStream();
        using var writer = new System.IO.BinaryWriter(stream);

        // Outer property tag (v1.2): name, type tree {ArrayProperty -> StructProperty -> SplinePointData},
        // binarySize, flags=0.
        writer.WriteFString("mSplineData");
        writer.WriteFString("ArrayProperty"); writer.Write(1);   // 1 child
        writer.WriteFString("StructProperty"); writer.Write(1);  // 1 child
        writer.WriteFString("SplinePointData"); writer.Write(0); // leaf

        var binarySizeOffset = stream.Position;
        writer.Write(0);                                          // binarySize placeholder
        writer.Write((byte)0);                                    // flags

        var valueStart = stream.Position;
        writer.Write(2);                                          // count = 2 elements

        // Element 1: a single inner IntProperty "Step" = 7, then "None" terminator.
        writer.WriteFString("Step");
        writer.WriteFString("IntProperty"); writer.Write(0);
        writer.Write(4);                                          // inner binarySize
        writer.Write((byte)0);                                    // inner flags
        writer.Write(7);                                          // inner value
        writer.WriteFString("None");

        // Element 2: IntProperty "Step" = 11, "None"
        writer.WriteFString("Step");
        writer.WriteFString("IntProperty"); writer.Write(0);
        writer.Write(4);
        writer.Write((byte)0);
        writer.Write(11);
        writer.WriteFString("None");

        var binarySize = (int)(stream.Position - valueStart);
        stream.Position = binarySizeOffset;
        writer.Write(binarySize);

        // Sentinel after the value bytes — must survive untouched (fence ends here).
        stream.Position = stream.Length;
        writer.Write(0xABCD1234u);

        stream.Position = 0;
        using var reader = new System.IO.BinaryReader(stream);
        var prop = (RawProperty)PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: V12)!;

        prop.Type.Should().Be("ArrayProperty");
        prop.ArrayStructValues.Should().NotBeNull();
        prop.ArrayStructValues!.Should().HaveCount(2);

        prop.ArrayStructValues[0].Properties.Should().HaveCount(1);
        ((RawProperty)prop.ArrayStructValues[0].Properties[0]).Name.Should().Be("Step");
        ((RawProperty)prop.ArrayStructValues[0].Properties[0]).IntValue.Should().Be(7);

        ((RawProperty)prop.ArrayStructValues[1].Properties[0]).IntValue.Should().Be(11);

        reader.ReadUInt32().Should().Be(0xABCD1234u, "the outer fence must align after the element bodies");
    }

    [Test]
    public void DeserializeProperty_AtV12_ArrayOfStruct_EmptyArray_ProducesEmptyValuesList()
    {
        using var stream = new System.IO.MemoryStream();
        using var writer = new System.IO.BinaryWriter(stream);

        writer.WriteFString("mSplineData");
        writer.WriteFString("ArrayProperty"); writer.Write(1);
        writer.WriteFString("StructProperty"); writer.Write(1);
        writer.WriteFString("SplinePointData"); writer.Write(0);

        var binarySizeOffset = stream.Position;
        writer.Write(0);
        writer.Write((byte)0);

        var valueStart = stream.Position;
        writer.Write(0);                                          // count = 0

        var binarySize = (int)(stream.Position - valueStart);
        stream.Position = binarySizeOffset;
        writer.Write(binarySize);

        stream.Position = 0;
        using var reader = new System.IO.BinaryReader(stream);
        var prop = (RawProperty)PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: V12)!;

        prop.ArrayStructValues.Should().NotBeNull().And.BeEmpty();
    }

    [Test]
    public void DeserializeProperties_AtV12_ReadsMultiplePropertiesThenStopsOnNone()
    {
        using var reader = MakeReader(w =>
        {
            w.Write((byte)0xAA);         // serializationControl
            // property 1: Int "A" = 1
            w.WriteFString("A");
            w.WriteFString("IntProperty"); w.Write(0);
            w.Write(4); w.Write((byte)0); w.Write(1);
            // property 2: Int "B" = 2
            w.WriteFString("B");
            w.WriteFString("IntProperty"); w.Write(0);
            w.Write(4); w.Write((byte)0); w.Write(2);
            // terminator
            w.WriteFString("None");
        });

        var props = PropertySerializer.Instance
            .DeserializeProperties(reader, saveVersion: V12)
            .Cast<RawProperty>()
            .ToArray();

        props.Should().HaveCount(2);
        props[0].Name.Should().Be("A");
        props[0].IntValue.Should().Be(1);
        props[1].Name.Should().Be("B");
        props[1].IntValue.Should().Be(2);
    }
}
