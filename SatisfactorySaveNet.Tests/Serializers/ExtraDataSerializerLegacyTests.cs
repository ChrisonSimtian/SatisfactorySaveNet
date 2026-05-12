using FluentAssertions;
using SatisfactorySaveNet.Abstracts.Extra;
using System.Linq;
using static SatisfactorySaveNet.Tests.Serializers.BinaryReaderHelpers;

namespace SatisfactorySaveNet.Tests.Serializers;

/// <summary>
/// Pins the pre-v1.2 <c>ExtraData</c> branches for every actor class the parser
/// special-cases. All tests synthesise the legacy wire format at
/// <c>SaveVersion = 45</c> (or lower where a sub-gate requires it) so the
/// <c>SaveVersion &gt;= 53</c> v1.2 branches are bypassed. Drift in any version
/// gate inside <c>ExtraDataSerializer.cs</c> surfaces here as a specific test
/// failure rather than only via real-fixture regressions.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
public class ExtraDataSerializerLegacyTests
{
    // Production typePaths sampled from KnownConstants. Any of the listed Mk
    // variants would do — picking Mk1 keeps the test reading straightforward.
    private const string ConveyorBeltMk1 = "/Game/FactoryGame/Buildable/Factory/ConveyorBeltMk1/Build_ConveyorBeltMk1.Build_ConveyorBeltMk1_C";
    private const string PowerLine       = "/Game/FactoryGame/Buildable/Factory/PowerLine/Build_PowerLine.Build_PowerLine_C";
    private const string Truck           = "/Game/FactoryGame/Buildable/Vehicle/Truck/BP_Truck.BP_Truck_C";
    private const string Locomotive      = "/Game/FactoryGame/Buildable/Vehicle/Train/Locomotive/BP_Locomotive.BP_Locomotive_C";
    private const string BlueprintGameState = "/Game/FactoryGame/-Shared/Blueprint/BP_GameState.BP_GameState_C";
    private const string PlayerState     = "/Game/FactoryGame/Character/Player/BP_PlayerState.BP_PlayerState_C";
    private const string CircuitSubsystem = "/Game/FactoryGame/-Shared/Blueprint/BP_CircuitSubsystem.BP_CircuitSubsystem_C";

    // ---- Conveyor (pre-v1.2 path, no items — exercises the count + nrElements prefix) --

    [Test]
    public void Conveyor_PreV12_EmptyItemList_ReadsCountAndZero()
    {
        using var reader = MakeReader(w =>
        {
            w.Write(0);   // count
            w.Write(0);   // nrElements
        });

        var data = (ConveyorData)ExtraDataSerializer.Instance
            .Deserialize(reader, ConveyorBeltMk1, MakeHeader(saveVersion: 45), expectedPosition: reader.BaseStream.Length)!;

        data.Count.Should().Be(0);
        data.Items.Should().BeEmpty();
        reader.BaseStream.Position.Should().Be(8, "the legacy conveyor preamble is count + nrElements");
    }

    [Test]
    public void Conveyor_PreV12_SingleLegacyItem_ReadsItemFields()
    {
        // At saveVersion < 44 each item is: ObjectRef (name) + ObjectRef (itemState) + Vec4BAs4I (4 bytes)
        using var reader = MakeReader(w =>
        {
            w.Write(0);                                  // count
            w.Write(1);                                  // nrElements
            w.WriteObjectReference("Persistent_Level", "/Game/Iron.Iron_C");
            w.WriteObjectReference("Persistent_Level", "ItemState_1");
            w.Write(new byte[] { 0x01, 0x02, 0x03, 0x04 });
        });

        var data = (ConveyorData)ExtraDataSerializer.Instance
            .Deserialize(reader, ConveyorBeltMk1, MakeHeader(saveVersion: 43), expectedPosition: reader.BaseStream.Length)!;

        data.Items.Should().HaveCount(1);
        var item = data.Items.Single();
        item.Name.PathName.Should().Be("/Game/Iron.Iron_C");
        item.ItemState!.PathName.Should().Be("ItemState_1");
    }

    // ---- PowerLine ---------------------------------------------------------

    [Test]
    public void PowerLine_PreV12_NoTranslations_AtV41Plus()
    {
        // saveVersion >= 41 → no cached Vec3 translations are read; only count + 2 refs.
        using var reader = MakeReader(w =>
        {
            w.Write(0);
            w.WriteObjectReference("Persistent_Level", "PoleA");
            w.WriteObjectReference("Persistent_Level", "PoleB");
        });

        var data = (PowerLineData)ExtraDataSerializer.Instance
            .Deserialize(reader, PowerLine, MakeHeader(saveVersion: 45), expectedPosition: reader.BaseStream.Length)!;

        data.Source.PathName.Should().Be("PoleA");
        data.Target.PathName.Should().Be("PoleB");
        data.SourceTranslation.Should().BeNull();
        data.TargetTranslation.Should().BeNull();
    }

    [Test]
    public void PowerLine_V33ThroughV40_ReadsCachedTranslations()
    {
        // saveVersion 33..40 → ALSO reads two Vec3 (float) translations.
        using var reader = MakeReader(w =>
        {
            w.Write(0);
            w.WriteObjectReference("Persistent_Level", "PoleA");
            w.WriteObjectReference("Persistent_Level", "PoleB");
            w.WriteVec3(1f, 2f, 3f);
            w.WriteVec3(4f, 5f, 6f);
        });

        var data = (PowerLineData)ExtraDataSerializer.Instance
            .Deserialize(reader, PowerLine, MakeHeader(saveVersion: 35), expectedPosition: reader.BaseStream.Length)!;

        data.SourceTranslation.Should().NotBeNull();
        data.SourceTranslation!.Value.X.Should().Be(1f);
        data.TargetTranslation!.Value.Z.Should().Be(6f);
    }

    // ---- Circuit -----------------------------------------------------------

    [Test]
    public void Circuit_PreV12_ReadsCountThenElements()
    {
        // Pre-v1.2: leading `count` Int32 (unknown purpose) precedes nrElements.
        using var reader = MakeReader(w =>
        {
            w.Write(99);                                 // count (legacy unknown)
            w.Write(1);                                  // nrElements
            w.Write(42);                                 // circuitId
            w.WriteObjectReference("Persistent_Level", "Net_1");
        });

        var data = (CircuitData)ExtraDataSerializer.Instance
            .Deserialize(reader, CircuitSubsystem, MakeHeader(saveVersion: 50), expectedPosition: reader.BaseStream.Length)!;

        data.Count.Should().Be(99);
        data.Circuits.Should().HaveCount(1);
        var c = data.Circuits.Single();
        c.CircuitId.Should().Be(42);
        c.ObjectReference.PathName.Should().Be("Net_1");
    }

    // ---- Vehicle (cargo block-size depends on saveVersion 41 boundary) -----

    [Test]
    public void Vehicle_PreV41_UsesFiftyThreeByteCargoBlock()
    {
        using var reader = MakeReader(w =>
        {
            w.Write(0);                                  // count
            w.Write(1);                                  // nrElements
            w.WriteFString("SteeringWheel");
            w.Write(new byte[53]);                       // pre-v41 cargo block size
        });

        var data = (VehicleData)ExtraDataSerializer.Instance
            .Deserialize(reader, Truck, MakeHeader(saveVersion: 40), expectedPosition: reader.BaseStream.Length)!;

        data.CargoObjects.Should().HaveCount(1);
        var cargo = data.CargoObjects.Single();
        cargo.Name.Should().Be("SteeringWheel");
        cargo.Unknown.Should().HaveLength(53, "pre-v41 cargo blocks are 53 bytes");
    }

    [Test]
    public void Vehicle_V41Plus_UsesOneHundredFiveByteCargoBlock()
    {
        using var reader = MakeReader(w =>
        {
            w.Write(0);
            w.Write(1);
            w.WriteFString("SteeringWheel");
            w.Write(new byte[105]);                      // v41+ cargo block size
        });

        var data = (VehicleData)ExtraDataSerializer.Instance
            .Deserialize(reader, Truck, MakeHeader(saveVersion: 45), expectedPosition: reader.BaseStream.Length)!;

        data.CargoObjects.Single().Unknown.Should().HaveLength(105, "v41+ cargo blocks expand to 105 bytes");
    }

    // ---- Locomotive (cargo + chain links) ----------------------------------

    [Test]
    public void Locomotive_V41Plus_NoCargo_ReadsChainRefs()
    {
        using var reader = MakeReader(w =>
        {
            w.Write(0);                                  // count
            w.Write(0);                                  // nrElements
            w.WriteObjectReference("Persistent_Level", "Prev");
            w.WriteObjectReference("Persistent_Level", "Next");
        });

        var data = (LocomotiveData)ExtraDataSerializer.Instance
            .Deserialize(reader, Locomotive, MakeHeader(saveVersion: 45), expectedPosition: reader.BaseStream.Length)!;

        data.CargoObjects.Should().BeEmpty();
        data.Previous.PathName.Should().Be("Prev");
        data.Next.PathName.Should().Be("Next");
    }

    // ---- Blueprint (no version gate) ---------------------------------------

    [Test]
    public void Blueprint_RoundTripsCountAndReferences()
    {
        using var reader = MakeReader(w =>
        {
            w.Write(7);                                  // count
            w.Write(2);                                  // nrElements
            w.WriteObjectReference("Persistent_Level", "A");
            w.WriteObjectReference("Persistent_Level", "B");
        });

        var data = (BlueprintData)ExtraDataSerializer.Instance
            .Deserialize(reader, BlueprintGameState, MakeHeader(saveVersion: 50), expectedPosition: reader.BaseStream.Length)!;

        data.Count.Should().Be(7);
        data.Objects.Select(o => o.PathName).Should().Equal("A", "B");
    }

    // ---- PlayerData (mode 248 — simplest mode) -----------------------------

    [Test]
    public void PlayerData_Mode248_ReadsEpicOnlineServicesId_BeforePipe()
    {
        // mode 248 layout (from current position): Int32 (unused) + byte=248 + FString
        // The id is the first segment of the FString split on '|'.
        const byte mode248 = 248;
        var bytes = BuildBytes(w =>
        {
            w.Write(0);
            w.Write(mode248);
            w.WriteFString("UserAlpha|UserBeta");
        });

        using var reader = MakeReader(w => w.Write(bytes));

        var data = (PlayerData)ExtraDataSerializer.Instance
            .Deserialize(reader, PlayerState, MakeHeader(saveVersion: 50), expectedPosition: bytes.Length)!;

        data.PlayerType.Should().Be(mode248);
        data.EpicOnlineServicesId.Should().Be("UserAlpha");
    }

    // ---- UnknownExtraData fallback -----------------------------------------

    [Test]
    public void UnknownExtraData_NonScriptTypePath_CapturesRemainderAsHex()
    {
        // typePath that doesn't match any specific branch AND doesn't have a
        // script-prefix → catch-all reads (expectedPosition - position) bytes as hex.
        const string typePath = "/Game/SomeBuilding/Build_SomeBuilding.Build_SomeBuilding_C";
        using var reader = MakeReader(w => w.Write(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE, 0x00, 0xFF, 0xAB, 0xCD }));

        var data = ExtraDataSerializer.Instance
            .Deserialize(reader, typePath, MakeHeader(saveVersion: 50), expectedPosition: 8);

        data.Should().BeOfType<UnknownExtraData>();
        ((UnknownExtraData)data!).Missing.Should().HaveLength(8);
    }

    [Test]
    public void UnknownExtraData_ScriptPrefixedAtV41Plus_SkipsEightBytesAndReturnsNull()
    {
        // bytesCount > 4 AND saveVersion >= 41 AND typePath starts with "/Script/FactoryGame.FG"
        // → seek 8 bytes forward, return null (the bytes are signal of a known unhandled struct).
        const string typePath = "/Script/FactoryGame.FGUnknownStuffSubsystem";
        using var reader = MakeReader(w => w.Write(new byte[16]));

        var data = ExtraDataSerializer.Instance
            .Deserialize(reader, typePath, MakeHeader(saveVersion: 45), expectedPosition: 16);

        data.Should().BeNull("the script-prefix branch returns null and just consumes 8 bytes");
        reader.BaseStream.Position.Should().Be(8);
    }

    // ---- ConveyorChainActor minimal shape (no conveyors, no items) ---------

    [Test]
    public void ConveyorChainActor_Empty_ReadsHeaderAndZeroCounts()
    {
        const string typePath = "/Script/FactoryGame.FGConveyorChainActor";
        using var reader = MakeReader(w =>
        {
            w.Write(1);                                  // count
            w.WriteObjectReference("Persistent_Level", "ChainRoot1");
            w.WriteObjectReference("Persistent_Level", "ChainRoot2");
            w.Write(0);                                  // conveyorCount
            w.Write(123.5f);                             // totalLength
            w.Write(0);                                  // numberItems
            w.Write(-1);                                 // headItemIndex
            w.Write(-1);                                 // tailItemIndex
            w.Write(0);                                  // itemCount
        });

        var data = (ConveyorChainActor)ExtraDataSerializer.Instance
            .Deserialize(reader, typePath, MakeHeader(saveVersion: 45), expectedPosition: reader.BaseStream.Length)!;

        data.Count.Should().Be(1);
        data.ConveyorActors.Should().BeEmpty();
        data.Items.Should().BeEmpty();
        data.TotalLength.Should().Be(123.5f);
        data.HeadItemIndex.Should().Be(-1);
        data.Unknown1.PathName.Should().Be("ChainRoot1");
        data.Unknown2.PathName.Should().Be("ChainRoot2");
    }

    // ---- LightweightBuildableSubsystem minimal shape (no objects) ----------

    [Test]
    public void LightweightBuildableSubsystem_Empty_ReadsRootIntsAndZeroObjects()
    {
        // At HeaderVersion >= 14 there is an additional lightWeightVersion Int32
        // after the leading unknown Int32. With objectCount = 0 the parser
        // returns without entering the nested instance loops — perfect smoke
        // test for the file's preamble.
        const string typePath = "/Script/FactoryGame.FGLightweightBuildableSubsystem";
        using var reader = MakeReader(w =>
        {
            w.Write(0);     // unknownRoot1
            w.Write(2);     // lightWeightVersion (only at HeaderVersion >= 14)
            w.Write(0);     // objectCount
        });

        var data = (LightweightBuildableSubsystem)ExtraDataSerializer.Instance
            .Deserialize(reader, typePath, MakeHeader(saveVersion: 60, headerVersion: 14), expectedPosition: reader.BaseStream.Length)!;

        data.Objects.Should().BeEmpty();
        reader.BaseStream.Position.Should().Be(12, "the preamble reads exactly 3 Int32 at HeaderVersion 14");
    }

    // ---- DroneStation v41+ empty (action queues both zero) -----------------

    [Test]
    public void DroneStation_V41Plus_EmptyActionQueues_ConsumesPreambleOnly()
    {
        const string typePath = "/Game/FactoryGame/Buildable/Factory/DroneStation/BP_DroneTransport.BP_DroneTransport_C";
        using var reader = MakeReader(w =>
        {
            w.Write(1);                                  // unknown1
            w.Write(2);                                  // unknown2
            w.Write(0);                                  // nrActiveActions
            w.Write(0);                                  // nrQueuedActions
        });

        var data = (DroneStationData)ExtraDataSerializer.Instance
            .Deserialize(reader, typePath, MakeHeader(saveVersion: 45), expectedPosition: reader.BaseStream.Length)!;

        data.ActiveActions.Should().BeEmpty();
        data.ActionQueue.Should().BeEmpty();
        data.Unknown1.Should().Be(1);
        data.Unknown2.Should().Be(2);
    }
}
