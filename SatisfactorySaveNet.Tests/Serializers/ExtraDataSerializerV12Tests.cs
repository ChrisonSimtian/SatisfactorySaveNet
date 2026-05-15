using FluentAssertions;
using SatisfactorySaveNet.Abstracts.Extra;
using System.Linq;
using static SatisfactorySaveNet.Tests.Serializers.BinaryReaderHelpers;

namespace SatisfactorySaveNet.Tests.Serializers;

/// <summary>
/// Pins the v1.2 <c>ExtraData</c> branches added under issue #64. The
/// <see cref="ObjectSerializer"/> gate routes FGConveyorChainActor through
/// <see cref="ExtraDataSerializer.Deserialize"/> at <c>SaveVersion &gt;= 53</c>
/// — these tests run the deserializer at <c>SaveVersion = 60</c> against
/// handcrafted bytes so a drift in the gate or wire-format assumption shows
/// up as a specific failure here rather than only via real-save regressions.
///
/// Assumption locked in by AnthorNet/SC-InteractiveMap's Read.js: the chain-
/// actor wire format did not change at v1.2. If a future game build diverges,
/// these tests will fail and the deserializer needs a version split.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
public class ExtraDataSerializerV12Tests
{
    private const string FGConveyorChainActor = "/Script/FactoryGame.FGConveyorChainActor";
    private const string FGConveyorChainActorMedium = "/Script/FactoryGame.FGConveyorChainActor_RepSizeMedium";

    [Test]
    public void ConveyorChainActor_V12_Empty_ReadsHeaderAndZeroCounts()
    {
        using var reader = MakeReader(w =>
        {
            w.Write(1);                                  // count
            w.WriteObjectReference("Persistent_Level", "ChainRoot1");
            w.WriteObjectReference("Persistent_Level", "ChainRoot2");
            w.Write(0);                                  // conveyorCount
            w.Write(0.0f);                               // totalLength
            w.Write(0);                                  // numberItems
            w.Write(-1);                                 // headItemIndex
            w.Write(-1);                                 // tailItemIndex
            w.Write(0);                                  // itemCount
        });

        var data = (ConveyorChainActor)ExtraDataSerializer.Instance
            .Deserialize(reader, FGConveyorChainActor, MakeHeader(saveVersion: 60), expectedPosition: reader.BaseStream.Length)!;

        data.Count.Should().Be(1);
        data.ConveyorActors.Should().BeEmpty();
        data.Items.Should().BeEmpty();
        data.HeadItemIndex.Should().Be(-1);
        data.TailItemIndex.Should().Be(-1);
        data.Unknown1.PathName.Should().Be("ChainRoot1");
        data.Unknown2.PathName.Should().Be("ChainRoot2");
    }

    [Test]
    public void ConveyorChainActor_V12_OneConveyor_OneSpline_NoItems_PopulatesAllFields()
    {
        using var reader = MakeReader(w =>
        {
            // Chain header
            w.Write(7);                                                       // count
            w.WriteObjectReference("Persistent_Level", "ChainRoot1");
            w.WriteObjectReference("Persistent_Level", "ChainRoot2");

            // Conveyors (one entry)
            w.Write(1);                                                       // conveyorCount
            w.WriteObjectReference("Persistent_Level", "Unknown_1");
            w.WriteObjectReference("Persistent_Level", "Build_ConveyorBeltMk1_C_42");
            w.Write(1);                                                       // splinesCount
            // Spline 0: location, arriveTangent, leaveTangent — each Vec3D (3× double)
            w.Write(10.0);  w.Write(20.0);  w.Write(30.0);
            w.Write(1.0);   w.Write(0.0);   w.Write(0.0);
            w.Write(-1.0);  w.Write(0.0);   w.Write(0.0);
            w.Write(0.0f);                                                    // offsetAtStart
            w.Write(0.0f);                                                    // startsAtLength
            w.Write(123.5f);                                                  // endsAtLength
            w.Write(-1);                                                      // firstItemIndex
            w.Write(-1);                                                      // lastItemIndex
            w.Write(0);                                                       // indexInChainArray

            // Chain footer
            w.Write(123.5f);                                                  // totalLength
            w.Write(0);                                                       // numberItems
            w.Write(-1);                                                      // headItemIndex
            w.Write(-1);                                                      // tailItemIndex
            w.Write(0);                                                       // itemCount
        });

        var data = (ConveyorChainActor)ExtraDataSerializer.Instance
            .Deserialize(reader, FGConveyorChainActorMedium, MakeHeader(saveVersion: 60), expectedPosition: reader.BaseStream.Length)!;

        data.Count.Should().Be(7);
        data.ConveyorActors.Should().HaveCount(1);
        var conv = data.ConveyorActors.First();
        conv.ConveyorBase.PathName.Should().Be("Build_ConveyorBeltMk1_C_42");
        conv.Splines.Should().HaveCount(1);

        var spline = conv.Splines.First();
        spline.Location.X.Should().Be(10.0);
        spline.Location.Y.Should().Be(20.0);
        spline.Location.Z.Should().Be(30.0);

        conv.EndsAtLength.Should().Be(123.5f);
        conv.IndexInChainArray.Should().Be(0);

        data.TotalLength.Should().Be(123.5f);
        data.Items.Should().BeEmpty();
    }
}
