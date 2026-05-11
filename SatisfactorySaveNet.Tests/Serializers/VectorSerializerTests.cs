using FluentAssertions;
using static SatisfactorySaveNet.Tests.Serializers.BinaryReaderHelpers;

namespace SatisfactorySaveNet.Tests.Serializers;

/// <summary>
/// Spot-checks one representative shape per arithmetic family (float, double, int,
/// quaternion-float, quaternion-double, color, byte-packed). All twelve overloads
/// share the same trivial read-N-primitives shape so testing every overload would
/// be busywork.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
public class VectorSerializerTests
{
    [Test]
    public void DeserializeVec3_ReadsThreeFloats()
    {
        using var reader = MakeReader(w => w.WriteVec3(1.5f, -2.25f, 3.125f));

        var v = VectorSerializer.Instance.DeserializeVec3(reader);

        v.X.Should().Be(1.5f);
        v.Y.Should().Be(-2.25f);
        v.Z.Should().Be(3.125f);
        reader.BaseStream.Position.Should().Be(12);
    }

    [Test]
    public void DeserializeVec3D_ReadsThreeDoubles()
    {
        using var reader = MakeReader(w => w.WriteVec3D(1.5, -2.25, 3.125));

        var v = VectorSerializer.Instance.DeserializeVec3D(reader);

        v.X.Should().Be(1.5);
        v.Y.Should().Be(-2.25);
        v.Z.Should().Be(3.125);
        reader.BaseStream.Position.Should().Be(24);
    }

    [Test]
    public void DeserializeVec4_ReadsFourFloats()
    {
        using var reader = MakeReader(w => w.WriteVec4(1f, 2f, 3f, 4f));

        var v = VectorSerializer.Instance.DeserializeVec4(reader);

        v.X.Should().Be(1f); v.Y.Should().Be(2f); v.Z.Should().Be(3f); v.W.Should().Be(4f);
    }

    [Test]
    public void DeserializeVec4D_ReadsFourDoubles()
    {
        using var reader = MakeReader(w =>
        {
            w.Write(1.0); w.Write(2.0); w.Write(3.0); w.Write(4.0);
        });

        var v = VectorSerializer.Instance.DeserializeVec4D(reader);

        v.X.Should().Be(1.0); v.Y.Should().Be(2.0); v.Z.Should().Be(3.0); v.W.Should().Be(4.0);
        reader.BaseStream.Position.Should().Be(32);
    }

    [Test]
    public void DeserializeVec2I_ReadsTwoInt32()
    {
        using var reader = MakeReader(w => { w.Write(7); w.Write(-3); });

        var v = VectorSerializer.Instance.DeserializeVec2I(reader);

        v.X.Should().Be(7); v.Y.Should().Be(-3);
    }

    [Test]
    public void DeserializeQuaternion_ReadsFourFloats()
    {
        using var reader = MakeReader(w => w.WriteVec4(0f, 0f, 0f, 1f)); // identity quat

        var q = VectorSerializer.Instance.DeserializeQuaternion(reader);

        q.X.Should().Be(0f); q.Y.Should().Be(0f); q.Z.Should().Be(0f); q.W.Should().Be(1f);
    }

    [Test]
    public void DeserializeQuaternionD_ReadsFourDoubles()
    {
        using var reader = MakeReader(w => { w.Write(0.0); w.Write(0.0); w.Write(0.0); w.Write(1.0); });

        var q = VectorSerializer.Instance.DeserializeQuaternionD(reader);

        q.X.Should().Be(0.0); q.W.Should().Be(1.0);
    }

    [Test]
    public void DeserializeColor4_ReadsFourFloats_AsRgba()
    {
        using var reader = MakeReader(w => w.WriteVec4(0.1f, 0.2f, 0.3f, 1.0f));

        var c = VectorSerializer.Instance.DeserializeColor4(reader);

        c.R.Should().Be(0.1f); c.G.Should().Be(0.2f); c.B.Should().Be(0.3f); c.A.Should().Be(1.0f);
    }

    [Test]
    public void DeserializeVec4BAs4I_ReadsFourSignedBytes()
    {
        using var reader = MakeReader(w => w.Write(new byte[] { 0x01, 0xFF, 0x7F, 0x80 }));
        // 0xFF = -1, 0x80 = -128 when read as sbyte

        var v = VectorSerializer.Instance.DeserializeVec4BAs4I(reader);

        v.X.Should().Be(1);
        v.Y.Should().Be(-1);
        v.Z.Should().Be(127);
        v.W.Should().Be(-128);
        reader.BaseStream.Position.Should().Be(4);
    }
}
