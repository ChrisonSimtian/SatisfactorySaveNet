using FluentAssertions;
using NSubstitute;
using SatisfactorySaveNet.Abstracts;
using SatisfactorySaveNet.Abstracts.Exceptions;
using SatisfactorySaveNet.Abstracts.Model;
using System.IO;

namespace SatisfactorySaveNet.Tests.Serializers;

/// <summary>
/// Sanity-checks the SaveFileSerializer wiring around v1.2 — empty-stream guard +
/// the pre-v21 path skipping the FSaveObjectVersionData hook entirely. The full
/// SaveVersion &gt;= 53 compressed-body path is covered by real .sav fixtures in
/// <see cref="Fixtures.RealSaveFixtureTests"/> rather than synthesised here, because
/// the production code does its own zlib decompression and dataLength fence that's
/// not worth mocking around.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
public class SaveFileSerializerV12Tests
{
    [Test]
    public void Deserialize_EmptyStream_Throws()
    {
        using var stream = new MemoryStream();

        var act = () => SaveFileSerializer.Instance.Deserialize(stream);

        act.Should().Throw<CorruptedSatisFactorySaveFileException>();
    }

    [Test]
    public void Deserialize_PreV21Header_DoesNotInvokeVersionDataSerializer()
    {
        var versionData = Substitute.For<IFSaveObjectVersionDataSerializer>();
        var bodySerializer = Substitute.For<IBodySerializer>();
        var headerSerializer = Substitute.For<IHeaderSerializer>();
        headerSerializer.Deserialize(Arg.Any<BinaryReader>()).Returns(
            new Header { HeaderVersion = 4, SaveVersion = 10, BuildVersion = 0 });

        var serializer = new SaveFileSerializer(
            headerSerializer, ChunkSerializer.Instance, bodySerializer, versionData);

        using var stream = new MemoryStream(new byte[16]); // any non-empty stream
        _ = serializer.Deserialize(stream);

        versionData.DidNotReceive().Deserialize(Arg.Any<BinaryReader>());
        bodySerializer.Received(1).Deserialize(Arg.Any<BinaryReader>(), Arg.Any<Header>());
    }
}
