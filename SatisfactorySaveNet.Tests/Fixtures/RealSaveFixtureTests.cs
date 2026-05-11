using FluentAssertions;
using SatisfactorySaveNet.Abstracts;
using SatisfactorySaveNet.Abstracts.Model;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SatisfactorySaveNet.Tests.Fixtures;

/// <summary>
/// Integration tests that exercise the parser end-to-end against curated <c>.sav</c>
/// fixtures committed under <see cref="FixturesDirectoryName"/>. Each fixture filename
/// encodes the game version it was produced on, e.g. <c>1.2.0-empty-world.sav</c> —
/// the on-disk SaveCustomVersion is asserted from within the test rather than the name.
///
/// The discovery test uses a <see cref="TestCaseSource"/> that walks the fixtures
/// directory at runtime, so the suite passes vacuously when no fixtures exist and
/// gains coverage automatically as files are dropped in.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
public class RealSaveFixtureTests
{
    private const string FixturesDirectoryName = "Fixtures";

    private static ISaveFileSerializer Serializer => SaveFileSerializer.Instance;

    private static string FixturesDirectory =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, FixturesDirectoryName);

    public static IEnumerable<TestCaseData> AllFixtures()
    {
        if (!Directory.Exists(FixturesDirectory))
            yield break;

        foreach (var path in Directory.EnumerateFiles(FixturesDirectory, "*.sav"))
            yield return new TestCaseData(path).SetName($"{nameof(Deserialize_DoesNotThrow)}({Path.GetFileName(path)})");
    }

    [Test]
    [TestCaseSource(nameof(AllFixtures))]
    public void Deserialize_DoesNotThrow(string fixturePath)
    {
        var act = () => Serializer.Deserialize(fixturePath);

        act.Should().NotThrow();
    }

    // -------------------------------------------------------------------------
    // Named-fixture assertion tests for the handcrafted v1.2 saves. The smoke
    // test above already covers parse-without-throwing; these lock in the
    // structural identity of each fixture so regressions in the v1.2 parser
    // show up as a specific test failure rather than just a parse exception.
    // -------------------------------------------------------------------------

    // Recorded against the handcrafted fixtures on 2026-05-12. If you re-create the
    // fixtures from a newer game build these will drift — update both values and
    // bump the comment date.
    private const int ExpectedHeaderVersion = 14;
    private const int ExpectedSaveVersion = 60;
    private const int ExpectedBuildVersion = 489969;
    private const string ExpectedMapName = "Persistent_Level";
    private const string ExpectedSessionName = "SatisfactorySaveNet - v1.2";

    private const string EmptyWorldFile = ExpectedSessionName + " - Empty World.sav";
    private const string TheHubFile = ExpectedSessionName + " - The Hub.sav";

    [Test]
    public void Fixture_V12_EmptyWorld_HasExpectedShape()
    {
        var save = Serializer.Deserialize(Path.Combine(FixturesDirectory, EmptyWorldFile));
        var body = AssertCommonV12Shape(save, EmptyWorldFile);

        save.Header.PlayedSeconds.Should().Be(8);
        body.Levels.Should().HaveCount(209,
            "the empty world fixture has 209 levels (persistent + 208 sublevels from world partitioning)");

        var totalObjects = body.Levels.Sum(l => l.Objects.Count);
        TestContext.Out.WriteLine($"  TotalObjects  : {totalObjects}");
        totalObjects.Should().Be(1252,
            "an empty v1.2 save still ships the world's pre-placed entities");
    }

    [Test]
    public void Fixture_V12_TheHub_HasExpectedShape()
    {
        var save = Serializer.Deserialize(Path.Combine(FixturesDirectory, TheHubFile));
        var body = AssertCommonV12Shape(save, TheHubFile);

        save.Header.PlayedSeconds.Should().Be(77);
        body.Levels.Should().HaveCount(228,
            "placing The Hub causes the game to instantiate additional partition sublevels");

        var totalObjects = body.Levels.Sum(l => l.Objects.Count);
        TestContext.Out.WriteLine($"  TotalObjects  : {totalObjects}");
        totalObjects.Should().Be(1311,
            "The Hub fixture's recorded object total — drift here = parser regression");

        // Differential invariant: The Hub must always have strictly more parsed
        // objects than Empty World. Survives small game-build version bumps better
        // than the absolute count.
        var emptySave = Serializer.Deserialize(Path.Combine(FixturesDirectory, EmptyWorldFile));
        var emptyTotal = ((BodyV8)emptySave.Body!).Levels.Sum(l => l.Objects.Count);
        totalObjects.Should().BeGreaterThan(emptyTotal,
            "placing The Hub must yield strictly more parsed objects than an empty world");
    }

    private static BodyV8 AssertCommonV12Shape(Abstracts.Model.SatisfactorySave save, string fixtureName)
    {
        TestContext.Out.WriteLine($"--- {fixtureName} ---");
        TestContext.Out.WriteLine($"  HeaderVersion : {save.Header.HeaderVersion}");
        TestContext.Out.WriteLine($"  SaveVersion   : {save.Header.SaveVersion}");
        TestContext.Out.WriteLine($"  BuildVersion  : {save.Header.BuildVersion}");
        TestContext.Out.WriteLine($"  MapName       : {save.Header.MapName}");
        TestContext.Out.WriteLine($"  SessionName   : {save.Header.SessionName}");
        TestContext.Out.WriteLine($"  PlayedSeconds : {save.Header.PlayedSeconds}");

        save.Header.HeaderVersion.Should().Be(ExpectedHeaderVersion);
        save.Header.SaveVersion.Should().Be(ExpectedSaveVersion,
            "the v1.2 handcrafted fixtures were saved at SaveCustomVersion 60");
        save.Header.BuildVersion.Should().Be(ExpectedBuildVersion);
        save.Header.MapName.Should().Be(ExpectedMapName);
        save.Header.SessionName.Should().Be(ExpectedSessionName);
        save.Body.Should().BeOfType<BodyV8>();

        var body = (BodyV8)save.Body!;
        TestContext.Out.WriteLine($"  LevelCount    : {body.Levels.Count}");
        return body;
    }
}
