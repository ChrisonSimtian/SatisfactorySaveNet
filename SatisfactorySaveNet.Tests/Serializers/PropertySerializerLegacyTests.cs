using FluentAssertions;
using SatisfactorySaveNet.Abstracts.Model;
using SatisfactorySaveNet.Abstracts.Model.Properties;
using System.Linq;
using static SatisfactorySaveNet.Tests.Serializers.BinaryReaderHelpers;

namespace SatisfactorySaveNet.Tests.Serializers;

/// <summary>
/// Exercises the pre-v1.2 short-tag property path
/// (<c>PropertySerializer.cs:81-134</c> → switch at <c>:136-160</c>). All tests
/// pass <c>saveVersion = 50</c> so the v1.2 RawProperty branch is bypassed and
/// the typed <c>Deserialize&lt;Type&gt;Property</c> methods run. This is the
/// v1.1-compatibility canary: if a v1.2 commit silently regresses the legacy
/// dispatch, one of these fails first.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
public class PropertySerializerLegacyTests
{
    private const int Legacy = 50;

    // Header is required for ArrayProperty / StructProperty / MapProperty /
    // TextProperty dispatch but the scalar property tests don't need it.
    private static readonly Abstracts.Model.Header HeaderV11 = MakeHeader(saveVersion: Legacy);

    // ---- scalar properties ------------------------------------------------

    [Test]
    public void Bool_True_RoundTrips()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("IsActive");
            w.WriteFString("BoolProperty");
            w.Write(0);           // binarySize (unused — bool has no value bytes)
            w.Write(7);           // index
            w.Write((sbyte)1);    // value
            w.Write((sbyte)0);    // padding
        });

        var prop = (BoolProperty)PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: Legacy)!;

        prop.Name.Should().Be("IsActive");
        prop.Index.Should().Be(7);
        prop.Value.Should().Be(1);
    }

    [Test]
    public void Int_RoundTrips()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Money");
            w.WriteFString("IntProperty");
            w.Write(4);           // binarySize
            w.Write(0);           // index
            w.Write((sbyte)0);    // padding
            w.Write(12_345);
        });

        var prop = (IntProperty)PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: Legacy)!;

        prop.Name.Should().Be("Money");
        prop.Value.Should().Be(12_345);
    }

    [Test]
    public void Int8_RoundTrips()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Tier");
            w.WriteFString("Int8Property");
            w.Write(1);
            w.Write(0);
            w.Write((sbyte)0);
            w.Write((sbyte)-42);
        });

        var prop = (Int8Property)PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: Legacy)!;

        prop.Value.Should().Be(-42);
    }

    [Test]
    public void Int64_RoundTrips()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Timestamp");
            w.WriteFString("Int64Property");
            w.Write(8);
            w.Write(0);
            w.Write((sbyte)0);
            w.Write(1_700_000_000_000L);
        });

        var prop = (Int64Property)PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: Legacy)!;

        prop.Value.Should().Be(1_700_000_000_000L);
    }

    [Test]
    public void UInt32_RoundTrips()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Flags");
            w.WriteFString("UInt32Property");
            w.Write(4);
            w.Write(0);
            w.Write((sbyte)0);
            w.Write(0xDEADBEEFu);
        });

        var prop = (UInt32Property)PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: Legacy)!;

        prop.Value.Should().Be(0xDEADBEEFu);
    }

    [Test]
    public void UInt64_RoundTrips()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("BigId");
            w.WriteFString("UInt64Property");
            w.Write(8);
            w.Write(0);
            w.Write((sbyte)0);
            w.Write(ulong.MaxValue - 1);
        });

        var prop = (UInt64Property)PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: Legacy)!;

        prop.Value.Should().Be(ulong.MaxValue - 1);
    }

    [Test]
    public void Float_RoundTrips()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Health");
            w.WriteFString("FloatProperty");
            w.Write(4);
            w.Write(0);
            w.Write((sbyte)0);
            w.Write(75.5f);
        });

        var prop = (FloatProperty)PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: Legacy)!;

        prop.Value.Should().Be(75.5f);
    }

    [Test]
    public void Double_RoundTrips()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Precision");
            w.WriteFString("DoubleProperty");
            w.Write(8);
            w.Write(0);
            w.Write((sbyte)0);
            w.Write(3.141592653589793);
        });

        var prop = (DoubleProperty)PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: Legacy)!;

        prop.Value.Should().Be(3.141592653589793);
    }

    // ---- Byte (two branches) ----------------------------------------------

    [Test]
    public void Byte_NoneType_ReadsLiteralSByte()
    {
        // When type == "None", the value is a single sbyte (DeserializeByteProperty:835-836).
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Tier");
            w.WriteFString("ByteProperty");
            w.Write(1);                  // binarySize
            w.Write(0);                  // index
            w.WriteFString("None");      // type discriminator
            w.Write((sbyte)0);           // padding
            w.Write((sbyte)42);          // value
        });

        var prop = (ByteProperty)PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: Legacy)!;

        prop.Type.Should().Be("None");
        prop.ByteData.Should().Be(42);
        prop.StringData.Should().BeNull();
    }

    [Test]
    public void Byte_EnumType_ReadsFramedString()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("State");
            w.WriteFString("ByteProperty");
            w.Write(0);
            w.Write(0);
            w.WriteFString("E_FactoryState");
            w.Write((sbyte)0);
            w.WriteFString("E_FactoryState::Active");
        });

        var prop = (ByteProperty)PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: Legacy)!;

        prop.Type.Should().Be("E_FactoryState");
        prop.ByteData.Should().BeNull();
        prop.StringData.Should().Be("E_FactoryState::Active");
    }

    // ---- strings & references ---------------------------------------------

    [Test]
    public void Name_RoundTrips()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("ItemName");
            w.WriteFString("NameProperty");
            w.Write(0);
            w.Write(0);
            w.Write((sbyte)0);
            w.WriteFString("Iron Plate");
        });

        var prop = (NameProperty)PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: Legacy)!;

        prop.Value.Should().Be("Iron Plate");
    }

    [Test]
    public void Str_RoundTrips()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Label");
            w.WriteFString("StrProperty");
            w.Write(0);
            w.Write(0);
            w.Write((sbyte)0);
            w.WriteFString("Hello world");
        });

        var prop = (StrProperty)PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: Legacy)!;

        prop.Value.Should().Be("Hello world");
    }

    [Test]
    public void Object_RoundTrips()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Reference");
            w.WriteFString("ObjectProperty");
            w.Write(0);
            w.Write(0);
            w.Write((sbyte)0);
            w.WriteObjectReference("Persistent_Level", "/Game/Foo.Foo_C");
        });

        var prop = (ObjectProperty)PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: Legacy)!;

        prop.Value.LevelName.Should().Be("Persistent_Level");
        prop.Value.PathName.Should().Be("/Game/Foo.Foo_C");
    }

    [Test]
    public void SoftObject_RoundTrips()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("SoftRef");
            w.WriteFString("SoftObjectProperty");
            w.Write(0);
            w.Write(0);
            w.Write((sbyte)0);
            w.WriteFString("Persistent_Level");
            w.WriteFString("/Game/Bar.Bar_C");
            w.WriteFString("Unknown");
        });

        var prop = (SoftObjectProperty)PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: Legacy)!;

        prop.Value.LevelName.Should().Be("Persistent_Level");
        prop.Value.PathName.Should().Be("/Game/Bar.Bar_C");
        // SoftObjectProperty.Value is typed as the base ObjectReference but the
        // serializer returns a SoftObjectReference — downcast to read Unknown1.
        ((SoftObjectReference)prop.Value).Unknown1.Should().Be("Unknown");
    }

    // ---- Enum -------------------------------------------------------------

    [Test]
    public void Enum_RoundTrips()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("DayPhase");
            w.WriteFString("EnumProperty");
            w.Write(0);
            w.Write(0);
            w.WriteFString("E_DayPhase");
            w.Write((sbyte)0);
            w.WriteFString("E_DayPhase::Day");
        });

        var prop = (EnumProperty)PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: Legacy)!;

        prop.Type.Should().Be("E_DayPhase");
        prop.Value.Should().Be("E_DayPhase::Day");
    }

    // ---- arrays (3 representative element types) --------------------------

    [Test]
    public void Array_OfInt_RoundTripsElements()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Slots");
            w.WriteFString("ArrayProperty");
            w.Write(0);                  // binarySize
            w.Write(0);                  // index
            w.WriteFString("IntProperty"); // element type
            w.Write((sbyte)0);           // padding
            w.Write(3);                  // length
            w.Write(10); w.Write(20); w.Write(30);
        });

        var prop = (ArrayProperty)PropertySerializer.Instance.DeserializeProperty(reader, header: HeaderV11, saveVersion: Legacy)!;
        var array = (ArrayIntProperty)prop.Property;

        prop.Type.Should().Be("IntProperty");
        array.Values.Should().Equal(10, 20, 30);
    }

    [Test]
    public void Array_OfStr_RoundTripsElements()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Tags");
            w.WriteFString("ArrayProperty");
            w.Write(0);
            w.Write(0);
            w.WriteFString("StrProperty");
            w.Write((sbyte)0);
            w.Write(2);
            w.WriteFString("alpha");
            w.WriteFString("beta");
        });

        var prop = (ArrayProperty)PropertySerializer.Instance.DeserializeProperty(reader, header: HeaderV11, saveVersion: Legacy)!;
        var array = (ArrayStrProperty)prop.Property;

        array.Values.Should().Equal("alpha", "beta");
    }

    [Test]
    public void Array_OfObject_RoundTripsReferences()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Refs");
            w.WriteFString("ArrayProperty");
            w.Write(0);
            w.Write(0);
            w.WriteFString("ObjectProperty");
            w.Write((sbyte)0);
            w.Write(2);
            w.WriteObjectReference("Persistent_Level", "A_1");
            w.WriteObjectReference("Persistent_Level", "B_1");
        });

        var prop = (ArrayProperty)PropertySerializer.Instance.DeserializeProperty(reader, header: HeaderV11, saveVersion: Legacy)!;
        var array = (ArrayObjectProperty)prop.Property;

        array.Values.Should().HaveCount(2);
        array.Values.ElementAt(0).PathName.Should().Be("A_1");
        array.Values.ElementAt(1).PathName.Should().Be("B_1");
    }

    [Test]
    public void Array_OfFloat_RoundTripsElements()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Speeds");
            w.WriteFString("ArrayProperty");
            w.Write(0); w.Write(0);
            w.WriteFString("FloatProperty");
            w.Write((sbyte)0);
            w.Write(3);
            w.Write(1.0f); w.Write(2.0f); w.Write(3.0f);
        });

        var prop = (ArrayProperty)PropertySerializer.Instance.DeserializeProperty(reader, header: HeaderV11, saveVersion: Legacy)!;

        ((ArrayFloatProperty)prop.Property).Values.Should().Equal(1.0f, 2.0f, 3.0f);
    }

    [Test]
    public void Array_OfBool_RoundTripsElements()
    {
        // ArrayBoolProperty reads `count` SIGNED bytes — not packed bits.
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Toggles");
            w.WriteFString("ArrayProperty");
            w.Write(0); w.Write(0);
            w.WriteFString("BoolProperty");
            w.Write((sbyte)0);
            w.Write(3);
            w.Write((sbyte)1); w.Write((sbyte)0); w.Write((sbyte)1);
        });

        var prop = (ArrayProperty)PropertySerializer.Instance.DeserializeProperty(reader, header: HeaderV11, saveVersion: Legacy)!;

        var array = (ArrayBoolProperty)prop.Property;
        array.Values.Should().HaveCount(3);
    }

    [Test]
    public void Array_OfInt64_RoundTripsElements()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Counters");
            w.WriteFString("ArrayProperty");
            w.Write(0); w.Write(0);
            w.WriteFString("Int64Property");
            w.Write((sbyte)0);
            w.Write(2);
            w.Write(100L); w.Write(-200L);
        });

        var prop = (ArrayProperty)PropertySerializer.Instance.DeserializeProperty(reader, header: HeaderV11, saveVersion: Legacy)!;

        ((ArrayInt64Property)prop.Property).Values.Should().Equal(100L, -200L);
    }

    [Test]
    public void Array_OfDouble_RoundTripsElements()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Precise");
            w.WriteFString("ArrayProperty");
            w.Write(0); w.Write(0);
            w.WriteFString("DoubleProperty");
            w.Write((sbyte)0);
            w.Write(2);
            w.Write(1.5); w.Write(-2.5);
        });

        var prop = (ArrayProperty)PropertySerializer.Instance.DeserializeProperty(reader, header: HeaderV11, saveVersion: Legacy)!;

        ((ArrayDoubleProperty)prop.Property).Values.Should().Equal(1.5, -2.5);
    }

    [Test]
    public void Array_OfEnum_RoundTripsFramedStrings()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("States");
            w.WriteFString("ArrayProperty");
            w.Write(0); w.Write(0);
            w.WriteFString("EnumProperty");
            w.Write((sbyte)0);
            w.Write(2);
            w.WriteFString("E_Foo::A");
            w.WriteFString("E_Foo::B");
        });

        var prop = (ArrayProperty)PropertySerializer.Instance.DeserializeProperty(reader, header: HeaderV11, saveVersion: Legacy)!;

        ((ArrayEnumProperty)prop.Property).Values.Should().Equal("E_Foo::A", "E_Foo::B");
    }

    [Test]
    public void Array_OfInterface_ReadsObjectReferences()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Ifaces");
            w.WriteFString("ArrayProperty");
            w.Write(0); w.Write(0);
            w.WriteFString("InterfaceProperty");
            w.Write((sbyte)0);
            w.Write(1);
            w.WriteObjectReference("Persistent_Level", "Iface_1");
        });

        var prop = (ArrayProperty)PropertySerializer.Instance.DeserializeProperty(reader, header: HeaderV11, saveVersion: Legacy)!;

        var array = (ArrayInterfaceProperty)prop.Property;
        array.Values.Should().HaveCount(1);
    }

    // ---- TextProperty historyType=0 (the simplest of five branches) -------

    [Test]
    public void Text_HistoryType0_ReadsNamespaceKeyAndValue()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Label");
            w.WriteFString("TextProperty");
            w.Write(0);                  // binarySize
            w.Write(0);                  // index
            w.Write((sbyte)0);           // padding
            w.Write(0);                  // flags
            w.Write((byte)0);            // historyType = Base (namespace + key + value)
            w.WriteFString("LangNamespace");
            w.WriteFString("Hub.Greeting");
            w.WriteFString("Hello, FICSIT.");
        });

        var prop = (TextProperty)PropertySerializer.Instance.DeserializeProperty(reader, header: HeaderV11, saveVersion: Legacy)!;

        prop.HistoryType.Should().Be(0);
        prop.NameSpace.Should().Be("LangNamespace");
        prop.Key.Should().Be("Hub.Greeting");
        prop.Value.Should().Be("Hello, FICSIT.");
    }

    [Test]
    public void Text_HistoryType11_ReadsTableIdAndTextKey()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("LocText");
            w.WriteFString("TextProperty");
            w.Write(0);
            w.Write(0);
            w.Write((sbyte)0);
            w.Write(0);
            w.Write((byte)11);           // historyType = StringTableEntry
            w.WriteFString("FactoryUI");
            w.WriteFString("Item.IronPlate.DisplayName");
        });

        var prop = (TextProperty)PropertySerializer.Instance.DeserializeProperty(reader, header: HeaderV11, saveVersion: Legacy)!;

        prop.HistoryType.Should().Be(11);
        prop.TableId.Should().Be("FactoryUI");
        prop.TextKey.Should().Be("Item.IronPlate.DisplayName");
    }
}
