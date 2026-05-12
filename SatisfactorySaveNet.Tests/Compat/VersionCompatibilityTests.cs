using FluentAssertions;
using SatisfactorySaveNet.Abstracts.Model;
using SatisfactorySaveNet.Abstracts.Model.Properties;
using System.Linq;
using SatisfactorySaveNet.Tests.Serializers;
using static SatisfactorySaveNet.Tests.Serializers.BinaryReaderHelpers;

namespace SatisfactorySaveNet.Tests.Compat;

/// <summary>
/// Cross-version dispatch tests — the v1.1↔v1.2 compatibility canary. Each test
/// pins a single version-gated decision in the parser by feeding bytes that are
/// only valid on ONE side of the gate, then asserting the parser produced the
/// expected variant. Flipping a constant (e.g. <c>&lt; 53</c> → <c>&lt;= 53</c>)
/// in any of the gated branches must surface here as a failure.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
public class VersionCompatibilityTests
{
    // ---- Property dispatch: legacy switch (saveVersion < 53) vs RawProperty (>= 53) ----

    [Test]
    public void Property_AtSaveVersion52_TakesLegacyTypedSwitch()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Money");
            w.WriteFString("IntProperty");
            w.Write(4);                // binarySize
            w.Write(0);                // index
            w.Write((sbyte)0);         // padding
            w.Write(42);
        });

        var prop = PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: 52);

        prop.Should().BeOfType<IntProperty>("saveVersion 52 must still take the legacy short-tag path");
        ((IntProperty)prop!).Value.Should().Be(42);
    }

    [Test]
    public void Property_AtSaveVersion53_TakesRawPropertyPath()
    {
        using var reader = MakeReader(w =>
        {
            w.WriteFString("Money");
            w.WriteFString("IntProperty"); w.Write(0);   // complete-tag node (name + 0 children)
            w.Write(4);                                   // binarySize
            w.Write((byte)0);                             // flags
            w.Write(42);                                  // value
        });

        var prop = PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: 53);

        prop.Should().BeOfType<RawProperty>("saveVersion 53 must switch to the complete-tag RawProperty path");
        ((RawProperty)prop!).Type.Should().Be("IntProperty");
        ((RawProperty)prop!).IntValue.Should().Be(42);
    }

    // ---- ObjectHeader flags: read at saveVersion >= 51 only --------------

    [Test]
    public void ObjectHeader_AtV50_DoesNotReadFlags()
    {
        // Pre-51 layout: type + FString TypePath + ObjectReference + Vec4 + Vec3 + Vec3 + Int32 + Int32
        // The Flags UInt32 is NOT present.
        using var reader = MakeReader(w =>
        {
            w.Write(ActorObject.TypeID);
            w.WriteFString("Foo_C");
            w.WriteObjectReference("Persistent_Level", "Foo_1");
            w.Write(0);                                // NeedTransform
            w.WriteVec4(0f, 0f, 0f, 1f);
            w.WriteVec3(0f, 0f, 0f);
            w.WriteVec3(1f, 1f, 1f);
            w.Write(1);                                // PlacedInLevel
        });

        var actor = (ActorObject)ObjectHeaderSerializer.Instance.Deserialize(reader, saveVersion: 50);

        actor.Flags.Should().BeNull("pre-v51 saves do not carry the Flags field");
        actor.PlacedInLevel.Should().Be(1, "the trailing Int32 must align without a Flags read");
    }

    [Test]
    public void ObjectHeader_AtV51_ReadsFlags()
    {
        using var reader = MakeReader(w =>
        {
            w.Write(ActorObject.TypeID);
            w.WriteFString("Foo_C");
            w.WriteObjectReference("Persistent_Level", "Foo_1");
            w.Write(0xCAFEBABEu);                      // Flags — only at v51+
            w.Write(0);
            w.WriteVec4(0f, 0f, 0f, 1f);
            w.WriteVec3(0f, 0f, 0f);
            w.WriteVec3(1f, 1f, 1f);
            w.Write(1);
        });

        var actor = (ActorObject)ObjectHeaderSerializer.Instance.Deserialize(reader, saveVersion: 51);

        actor.Flags.Should().Be(0xCAFEBABEu, "v51+ saves serialize a UInt32 Flags after the ObjectReference");
    }

    // ---- TypedData float-vs-double seam at SaveVersion 41 ----------------

    [Test]
    public void TypedData_Quat_AtV40_StaysOnFloatPath()
    {
        using var reader = MakeReader(w => w.WriteVec4(0f, 0f, 0f, 1f));

        var result = TypedDataSerializer.Instance.Deserialize(
            reader, MakeHeader(saveVersion: 40), "Quat", isArrayProperty: false, binarySize: 16);

        result.Should().BeOfType<Abstracts.Model.Typed.Quat>();
    }

    [Test]
    public void TypedData_Quat_AtV41_SwitchesToDoublePath()
    {
        using var reader = MakeReader(w => { w.Write(0.0); w.Write(0.0); w.Write(0.0); w.Write(1.0); });

        var result = TypedDataSerializer.Instance.Deserialize(
            reader, MakeHeader(saveVersion: 41), "Quat", isArrayProperty: false, binarySize: 32);

        result.Should().BeOfType<Abstracts.Model.Typed.QuatD>();
    }

    // ---- Circuit ExtraData: leading `count` Int32 present pre-v53 only ---

    [Test]
    public void CircuitData_AtV52_ReadsLegacyLeadingCount()
    {
        // legacy: count Int32, nrElements Int32, then circuits
        using var reader = MakeReader(w =>
        {
            w.Write(123);     // count (legacy unknown)
            w.Write(0);       // nrElements (no circuits)
        });

        var data = (Abstracts.Extra.CircuitData)ExtraDataSerializer.Instance.Deserialize(
            reader,
            "/Game/FactoryGame/-Shared/Blueprint/BP_CircuitSubsystem.BP_CircuitSubsystem_C",
            MakeHeader(saveVersion: 52),
            expectedPosition: reader.BaseStream.Length)!;

        data.Count.Should().Be(123, "the legacy CircuitData layout prefixes nrElements with an additional Int32");
    }

    [Test]
    public void CircuitData_AtV53_DropsLegacyLeadingCount()
    {
        // v1.2: only nrElements Int32, then circuits (no preceding `count`).
        using var reader = MakeReader(w =>
        {
            w.Write(0);       // nrElements (no preceding count Int32)
        });

        var data = (Abstracts.Extra.CircuitData)ExtraDataSerializer.Instance.Deserialize(
            reader,
            "/Game/FactoryGame/-Shared/Blueprint/BP_CircuitSubsystem.BP_CircuitSubsystem_C",
            MakeHeader(saveVersion: 53),
            expectedPosition: reader.BaseStream.Length)!;

        data.Count.Should().Be(0, "v1.2+ drops the legacy leading count, leaving Count at its default");
        data.Circuits.Should().BeEmpty();
    }

    // ---- ConveyorData payload: full item list pre-v53 vs single Int32 v53+ ----

    [Test]
    public void ConveyorData_AtV53_ConsumesOnlySingleInt32()
    {
        const string typePath = "/Game/FactoryGame/Buildable/Factory/ConveyorBeltMk1/Build_ConveyorBeltMk1.Build_ConveyorBeltMk1_C";
        using var reader = MakeReader(w =>
        {
            w.Write(0);                  // the single discard read at v1.2+
            w.Write(0xDEADBEEFu);        // sentinel that must remain unread
        });

        var data = (Abstracts.Extra.ConveyorData)ExtraDataSerializer.Instance.Deserialize(
            reader, typePath, MakeHeader(saveVersion: 53), expectedPosition: reader.BaseStream.Length)!;

        data.Items.Should().BeEmpty();
        reader.BaseStream.Position.Should().Be(4, "v1.2+ ConveyorData reads exactly one Int32");
        reader.ReadUInt32().Should().Be(0xDEADBEEFu);
    }
}
