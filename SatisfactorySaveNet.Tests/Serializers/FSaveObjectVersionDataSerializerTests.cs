using FluentAssertions;
using SatisfactorySaveNet.Abstracts.Model;
using System;
using static SatisfactorySaveNet.Tests.Serializers.BinaryReaderHelpers;

namespace SatisfactorySaveNet.Tests.Serializers;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public class FSaveObjectVersionDataSerializerTests
{
    private static readonly Guid GuidA = new("11111111-2222-3333-4444-555555555555");
    private static readonly Guid GuidB = new("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Test]
    public void Deserialize_ReadsAllScalarsAndEmptyCustomVersionContainer()
    {
        using var reader = MakeReader(w =>
        {
            w.Write(7u);                 // SaveObjectVersionDataVersion
            w.Write(518);                // FPackageFileVersion.Ue4Version
            w.Write(1004);               // FPackageFileVersion.Ue5Version
            w.Write(42);                 // LicenceVersion
            w.Write((ushort)5);          // EngineVersion.Major
            w.Write((ushort)3);          // EngineVersion.Minor
            w.Write((ushort)2);          // EngineVersion.Patch
            w.Write(12345u);             // EngineVersion.Changelist
            w.WriteFString("++UE5+Release-5.3");
            w.Write(0);                  // count of custom versions
        });

        var result = FSaveObjectVersionDataSerializer.Instance.Deserialize(reader);

        result.SaveObjectVersionDataVersion.Should().Be(7u);
        result.PackageFileVersion.Ue4Version.Should().Be(518);
        result.PackageFileVersion.Ue5Version.Should().Be(1004);
        result.LicenceVersion.Should().Be(42);
        result.EngineVersion.Major.Should().Be(5);
        result.EngineVersion.Minor.Should().Be(3);
        result.EngineVersion.Patch.Should().Be(2);
        result.EngineVersion.Changelist.Should().Be(12345u);
        result.EngineVersion.Branch.Should().Be("++UE5+Release-5.3");
        result.CustomVersionContainer.Versions.Should().BeEmpty();
    }

    [Test]
    public void Deserialize_ReadsMultipleCustomVersionsInOrder()
    {
        using var reader = MakeReader(w =>
        {
            w.Write(7u);
            w.Write(0); w.Write(0);
            w.Write(0);
            w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0);
            w.Write(0u);
            w.WriteFString("");
            w.Write(2);                  // count
            w.WriteGuid(GuidA); w.Write(11);
            w.WriteGuid(GuidB); w.Write(22);
        });

        var result = FSaveObjectVersionDataSerializer.Instance.Deserialize(reader);

        var versions = new System.Collections.Generic.List<FCustomVersion>(result.CustomVersionContainer.Versions);
        versions.Should().HaveCount(2);
        versions[0].Guid.Should().Be(GuidA);
        versions[0].Version.Should().Be(11);
        versions[1].Guid.Should().Be(GuidB);
        versions[1].Version.Should().Be(22);
    }

    [Test]
    public void Deserialize_EmptyBranchString_RoundTrips()
    {
        using var reader = MakeReader(w =>
        {
            w.Write(0u);
            w.Write(0); w.Write(0);
            w.Write(0);
            w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0);
            w.Write(0u);
            w.WriteFString("");          // empty branch
            w.Write(0);
        });

        var result = FSaveObjectVersionDataSerializer.Instance.Deserialize(reader);

        result.EngineVersion.Branch.Should().BeEmpty();
    }

    [Test]
    public void Deserialize_StreamIsFullyConsumed()
    {
        var bytes = BuildBytes(w =>
        {
            w.Write(1u);
            w.Write(518); w.Write(1004);
            w.Write(0);
            w.Write((ushort)5); w.Write((ushort)0); w.Write((ushort)0);
            w.Write(0u);
            w.WriteFString("main");
            w.Write(1);
            w.WriteGuid(GuidA); w.Write(99);
        });

        using var reader = MakeReader(w => w.Write(bytes));

        _ = FSaveObjectVersionDataSerializer.Instance.Deserialize(reader);

        reader.BaseStream.Position.Should().Be(bytes.Length,
            "the v1.2 block must be fully consumed so downstream readers stay aligned");
    }
}
