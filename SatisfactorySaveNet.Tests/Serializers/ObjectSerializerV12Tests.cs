using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SatisfactorySaveNet.Abstracts;
using SatisfactorySaveNet.Abstracts.Model;
using System;
using System.IO;
using static SatisfactorySaveNet.Tests.Serializers.BinaryReaderHelpers;

namespace SatisfactorySaveNet.Tests.Serializers;

/// <summary>
/// Targeted tests for the two helpers ObjectSerializer added for v1.2 — the
/// post-property hasGuid flag and the post-body FSaveObjectVersionData hook.
/// Pre-v1.2 invocation must be a no-op so legacy saves keep parsing.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
public class ObjectSerializerV12Tests
{
    private const int V12 = 53;

    private static ObjectSerializer NewSerializer(IFSaveObjectVersionDataSerializer versionData) =>
        new(
            NullLoggerFactory.Instance,
            StringSerializer.Instance,
            ObjectReferenceSerializer.Instance,
            PropertySerializer.Instance,
            ExtraDataSerializer.Instance,
            HexSerializer.Instance,
            versionData);

    private static ComponentObject NewComponentObject() =>
        new() { ObjectReference = new ObjectReference { LevelName = string.Empty, PathName = string.Empty } };

    [TestCase(0)]
    [TestCase(40)]
    [TestCase(52)]
    public void ReadOptionalObjectGuid_PreV12_IsNoOp(int objectSaveVersion)
    {
        using var reader = MakeReader(w => w.Write(0xDEADBEEFu)); // sentinel bytes must remain unread

        ObjectSerializer.ReadOptionalObjectGuid(reader, objectSaveVersion);

        reader.BaseStream.Position.Should().Be(0, "pre-v1.2 saves don't write the hasGuid flag");
    }

    [Test]
    public void ReadOptionalObjectGuid_HasGuidZero_ConsumesOnlyTheFlag()
    {
        using var reader = MakeReader(w =>
        {
            w.Write(0);                  // hasGuid = 0
            w.Write(0xDEADBEEFu);        // sentinel — must NOT be read
        });

        ObjectSerializer.ReadOptionalObjectGuid(reader, V12);

        reader.BaseStream.Position.Should().Be(4);
        reader.ReadUInt32().Should().Be(0xDEADBEEFu, "the GUID branch must not consume sentinel bytes");
    }

    [Test]
    public void ReadOptionalObjectGuid_HasGuidOne_ConsumesFlagPlusSixteenBytes()
    {
        var guid = Guid.NewGuid();
        using var reader = MakeReader(w =>
        {
            w.Write(1);                  // hasGuid = 1
            w.WriteGuid(guid);           // 16 bytes
            w.Write(0xDEADBEEFu);        // sentinel
        });

        ObjectSerializer.ReadOptionalObjectGuid(reader, V12);

        reader.BaseStream.Position.Should().Be(4 + 16);
        reader.ReadUInt32().Should().Be(0xDEADBEEFu);
    }

    [TestCase(0)]
    [TestCase(40)]
    [TestCase(52)]
    public void ReadOptionalPostBodyVersionData_PreV12_IsNoOp(int objectSaveVersion)
    {
        var versionData = Substitute.For<IFSaveObjectVersionDataSerializer>();
        var serializer = NewSerializer(versionData);
        var obj = NewComponentObject();
        using var reader = MakeReader(w => w.Write(0xDEADBEEFu));

        serializer.ReadOptionalPostBodyVersionData(reader, obj, objectSaveVersion);

        reader.BaseStream.Position.Should().Be(0);
        obj.ObjectVersionData.Should().BeNull();
        versionData.DidNotReceive().Deserialize(Arg.Any<BinaryReader>());
    }

    [Test]
    public void ReadOptionalPostBodyVersionData_ShouldSerializeZero_ConsumesOnlyTheFlag()
    {
        var versionData = Substitute.For<IFSaveObjectVersionDataSerializer>();
        var serializer = NewSerializer(versionData);
        var obj = NewComponentObject();
        using var reader = MakeReader(w =>
        {
            w.Write(0);                  // shouldSerialize = 0
            w.Write(0xDEADBEEFu);        // sentinel
        });

        serializer.ReadOptionalPostBodyVersionData(reader, obj, V12);

        reader.BaseStream.Position.Should().Be(4);
        obj.ObjectVersionData.Should().BeNull();
        versionData.DidNotReceive().Deserialize(Arg.Any<BinaryReader>());
    }

    [Test]
    public void ReadOptionalPostBodyVersionData_ShouldSerializeOne_DelegatesAndAssigns()
    {
        var versionData = Substitute.For<IFSaveObjectVersionDataSerializer>();
        var stub = new FSaveObjectVersionData
        {
            PackageFileVersion = new FPackageFileVersion(),
            EngineVersion = new FEngineVersion(),
            CustomVersionContainer = new FCustomVersionContainer()
        };
        versionData.Deserialize(Arg.Any<BinaryReader>()).Returns(stub);

        var serializer = NewSerializer(versionData);
        var obj = NewComponentObject();
        using var reader = MakeReader(w => w.Write(1)); // shouldSerialize = 1

        serializer.ReadOptionalPostBodyVersionData(reader, obj, V12);

        obj.ObjectVersionData.Should().BeSameAs(stub);
        versionData.Received(1).Deserialize(Arg.Any<BinaryReader>());
    }
}
