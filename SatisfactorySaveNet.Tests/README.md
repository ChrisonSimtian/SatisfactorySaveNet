# SatisfactorySaveNet.Tests

NUnit + FluentAssertions + NSubstitute. Two complementary layers:

| Layer | Where | What it tests | Speed | Dependencies |
|---|---|---|---|---|
| 1. Synthesised binary | `Serializers/` | Specific serializer branches in isolation, by feeding a `BinaryReader` over a hand-built byte sequence | ms | None |
| 2. Real-fixture integration | `Fixtures/` | The full pipeline on curated `.sav` files captured from the game | seconds | `.sav` files copied to the output dir |

Synthesised tests pin individual branches (especially version-gated ones); fixture
tests pin end-to-end identity of known saves. Use both — neither is sufficient
alone.

---

## Running

```bash
dotnet test
```

From the solution root. No special flags needed. On a clean checkout the
fixture-discovery test runs vacuously if `Fixtures/*.sav` is empty; the
synthesised tests always run.

### Coverage

A `coverlet.runsettings.xml` is checked in at the solution root.

```bash
dotnet test --collect:"XPlat Code Coverage" --settings coverlet.runsettings.xml
```

Report lands at `SatisfactorySaveNet.Tests/TestResults/<guid>/coverage.opencover.xml`
(OpenCover XML). Visualise with `reportgenerator` or ingest into your CI's
coverage UI.

---

## Layer 1 — synthesised binary tests

### Why these exist

Real saves don't let you isolate a single code path. You can't construct a save
file where (say) only the `flags & 0x2` GUID branch in `PropertySerializer` runs
and nothing else. Synthesised tests build the exact byte sequence that drives
one branch, so a regression there fails one named test instead of breaking
twenty fixture tests at once.

### Layout

```
Serializers/
  BinaryReaderHelpers.cs                       ← test-only utility
  FSaveObjectVersionDataSerializerTests.cs     ← v1.2 (custom version 53+)
  PropertySerializerV12Tests.cs                ← v1.2 property tag format
  ObjectSerializerV12Tests.cs                  ← v1.2 object-level helpers
  SaveFileSerializerV12Tests.cs                ← entry-point guards
  StringSerializerTests.cs                     ← UTF-8 + Unicode + empty
  HexSerializerTests.cs                        ← length-bounded byte→char
  VectorSerializerTests.cs                     ← Vec/Quat/Color shapes
  ObjectReferenceSerializerTests.cs            ← two-FString pair
  SoftObjectReferenceSerializerTests.cs        ← three-FString shape
  ChunkSerializerTests.cs                      ← four Int32 round-trip
  ObjectHeaderSerializerTests.cs               ← Actor/Component + v51 Flags gate
  PropertySerializerLegacyTests.cs             ← pre-v1.2 typed switch (v1.1 canary)
  TypedDataSerializerVersionTests.cs           ← v41 double/float gate, v44 InventoryItem split
  TypedDataSerializerSimpleTypesTests.cs       ← no-version-gate struct types
  ExtraDataSerializerLegacyTests.cs            ← pre-v1.2 per-actor branches
Compat/
  VersionCompatibilityTests.cs                 ← cross-version dispatch canary
```

### The helper

`BinaryReaderHelpers` is the only shared utility. Three things:

```csharp
// 1. Build a BinaryReader from a writer lambda — declarative byte layout.
using var reader = MakeReader(w =>
{
    w.Write(42);
    w.WriteFString("hello");
    w.WriteGuid(myGuid);
});

// 2. Build a byte[] for cases where you need to know the length.
var bytes = BuildBytes(w => w.Write(123));

// 3. WriteFString emits Unreal's length-prefixed UTF-8-with-null-terminator
//    format that StringSerializer reads back.
```

`WriteFString("")` collapses to `count=0` with no payload, matching production
saves. The encoding matches what `StringSerializer.Deserialize` consumes — no
Unicode (negative-count) path is currently used by tests but it would be a
straightforward extension.

### Adding a new test

1. Locate the production line you want to pin.
2. Read it carefully: which fields does it consume, in what order?
3. In `Serializers/<ClassName>Tests.cs`, add a `[Test]` method that builds the
   exact byte sequence via `MakeReader`, calls the production method, and
   asserts on the result + the stream position.
4. Add a sentinel value (`w.Write(0xDEADBEEFu)`) after the expected reads and
   assert it remains unread — this catches over-read bugs in the production code.

Concrete example, pinning the v1.2 `flag & 0x2` GUID branch in
`PropertySerializer`:

```csharp
[Test]
public void DeserializeProperty_AtV12_Flag0x2_ReadsPropertyGuid()
{
    var guid = Guid.NewGuid();
    using var reader = MakeReader(w =>
    {
        w.WriteFString("Tagged");
        w.WriteFString("IntProperty"); w.Write(0);  // tag node: name + 0 children
        w.Write(4);                                  // binarySize
        w.Write((byte)0x02);                         // flags: hasGuid only
        w.WriteGuid(guid);
        w.Write(42);                                 // value
    });

    var prop = (RawProperty)PropertySerializer.Instance.DeserializeProperty(reader, saveVersion: 53)!;

    prop.PropertyGuid.Should().Be(guid);
    prop.IntValue.Should().Be(42);
}
```

### `InternalsVisibleTo`

`SatisfactorySaveNet.csproj` exposes its internals to this test project via
`<AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">`.
This is used to test two `ObjectSerializer` helpers that are otherwise private:

- `ReadOptionalPostBodyVersionData` (instance method)
- `ReadOptionalObjectGuid` (static)

Both are marked `internal` rather than `private`. If you'd rather keep them
strictly private and test only via the public surface, the v1.2 fixture tests
exercise both transitively — the synthesised ones just isolate the branches.

---

## Layer 2 — real `.sav` fixture tests

### How discovery works

`RealSaveFixtureTests` has two parts:

1. **`Deserialize_DoesNotThrow`** — `[TestCaseSource(nameof(AllFixtures))]` walks
   `Fixtures/*.sav` at runtime and runs the parser on each. A new fixture file
   gets picked up automatically the next time the test binary is rebuilt (the
   `.sav` files are copied to the output dir via
   `<None Include="Fixtures\*.sav" CopyToOutputDirectory="PreserveNewest" />`).

2. **Named-fixture assertion tests** — one `[Test]` method per curated fixture
   with hand-recorded invariants. These guard the *identity* of each fixture:
   if the parser drifts and starts producing different object counts, the
   specific named test fails first.

### Fixture-naming convention

```
SatisfactorySaveNet - v<game-version> - <Scenario>.sav
```

Examples:
- `SatisfactorySaveNet - v1.2 - Empty World.sav`
- `SatisfactorySaveNet - v1.2 - The Hub.sav`
- `SatisfactorySaveNet - v1.2 - Pedestal.sav`

The on-disk `SaveCustomVersion` integer (53, 60, …) is **not** in the filename —
it's asserted from within the test, where it's much more discoverable for
someone reading the failure.

### Tracked vs. local-only

`Fixtures/.gitignore` is:

```
*.sav
!SatisfactorySaveNet - *.sav
```

Only files matching `SatisfactorySaveNet - *.sav` are tracked. Personal
autosaves dropped into the directory for local debugging stay out of git but
*do* get exercised by the smoke test locally. This lets contributors throw any
`.sav` they have at the parser without polluting the repo.

### Curated fixtures and their roles

| Fixture | Role |
|---|---|
| `Empty World` | Pristine v1.2 world, nothing built. The "zero" baseline — even an empty world serialises ~1252 pre-placed entities (nodes, biomes, fauna spawners). |
| `The Hub` | Just The Hub placed. Covers the starter building's parser path. |
| `Pedestal` | **Control base for derivative fixtures.** A small foundation structure in an otherwise pristine world. See below. |

### The Pedestal pattern

`Pedestal.sav` is meant as a controlled, minimal-but-non-empty test bed. The
intent is to load it in-game, place **one** building on the foundations, save as
`Pedestal + <Building>.sav`, and then write a differential test:

```csharp
[Test]
public void Fixture_V12_PedestalPlusMiner_HasExpectedShape()
{
    var save = Serializer.Deserialize(Path.Combine(FixturesDirectory, "SatisfactorySaveNet - v1.2 - Pedestal + Miner.sav"));
    var body = AssertCommonV12Shape(save, "Pedestal + Miner");

    var totalObjects = body.Levels.Sum(l => l.Objects.Count);

    // Differential: this fixture should differ from Pedestal by exactly the
    // objects the Miner placement instantiated.
    var pedestal = Serializer.Deserialize(Path.Combine(FixturesDirectory, "SatisfactorySaveNet - v1.2 - Pedestal.sav"));
    var pedestalTotal = ((BodyV8)pedestal.Body!).Levels.Sum(l => l.Objects.Count);

    (totalObjects - pedestalTotal).Should().Be(<N>,
        "placing a Miner instantiates the Miner actor plus its <N-1> components");
}
```

The delta is what's being tested — that's a clinical signal about whether a
specific building type's parser path still works. The absolute number drifts
across game builds; the *delta from Pedestal* is much more stable.

### Adding a new fixture

1. In-game, load the appropriate base save (`Pedestal` for "+ Building"
   fixtures, otherwise a fresh world).
2. Make the change (place exactly one building, build one belt loop, etc.).
3. Save with the naming convention above (`SatisfactorySaveNet - v1.2 - <Scenario>`
   in the in-game save dialog — the resulting `.sav` filename will match).
4. Run `./Update-Fixtures.ps1` (Windows only). The script reads the
   `ExpectedSessionName` constant from `RealSaveFixtureTests.cs`, then copies
   every `<session-name> - *.sav` it finds under
   `%LOCALAPPDATA%\FactoryGame\Saved\SaveGames` into `Fixtures/`. Pass
   `-WhatIf` for a dry run. If the script reports duplicates, it takes the
   most recently written copy.
5. Run `dotnet test --filter Deserialize_DoesNotThrow` — the smoke test picks
   up the new file automatically. If it passes, the fixture is at least
   parseable. If it throws, you've found a parser bug; isolate it with a
   synthesised test before committing the fixture.
6. Add a named-fixture `[Test]` method to `RealSaveFixtureTests`. Start with
   just `AssertCommonV12Shape` + `TestContext.Out.WriteLine` of the counts.
7. Run that single test once to read the observed values from the output.
8. Bake those numbers into the assertions. Lock the absolute count *and* the
   differential against Pedestal/EmptyWorld.

If you bump the session name (e.g. for a new game version) update
`ExpectedSessionName` in `RealSaveFixtureTests.cs` — both the C# tests and
`Update-Fixtures.ps1` pick the change up automatically.

### Why the absolute counts are still asserted

Even though deltas are more stable across game versions, the absolute number is
a regression catch for parser drift between game versions — if the parser
silently starts emitting two extra phantom objects for every save, the absolute
assertion fails first and you notice. The delta assertion catches
fixture-specific regressions; the absolute assertion catches global ones.

### Recorded baselines (v1.2, BuildVersion 489969, SaveVersion 60)

| Fixture | Levels | Total objects | Δ over Empty World |
|---|---:|---:|---:|
| Empty World | 209 | 1252 | — |
| Pedestal | 228 | 1295 | +43 |
| The Hub | 228 | 1311 | +59 |

If you re-record fixtures against a newer game build, update the constants at
the top of `RealSaveFixtureTests` (`ExpectedBuildVersion`, etc.) and re-baseline
these numbers. Bump the dated comment near those constants so future readers
know when the values were captured.

---

## Known parser quirk pinned by a regression test

`PropertySerializer.cs` gates the value-parsing step behind `if (binarySize > 0)`,
but the `BoolProperty` branch inside `TryParseKnownValue` doesn't read any
value bytes — the boolean lives in flag bit `0x10`. If a real save ever emits a
`BoolProperty` tag with `binarySize == 0`, `RawProperty.BoolValue` stays null.

The test `DeserializeProperty_AtV12_BoolProperty_WithZeroBinarySize_LeavesBoolValueNull`
pins this behaviour as a regression marker, **not** as a contract. If you move
the BoolProperty special-case before the gate (so the flag-bit value is read
even when `binarySize == 0`), tighten that test to assert the populated value
instead.

---

## Layer composition

The two layers complement each other deliberately:

- **Synthesised tests** cover *every* version-gated branch — v1.2 active paths
  and pre-v1.2 no-op paths. Real fixtures only exercise the active paths.
- **Fixture tests** cover the *integration* — header → chunked decompression →
  body → object → property → ExtraData, with all the position-fences and
  alignment fix-ups firing in concert. No synthesised test can do that.

If you're adding a feature: write the synthesised test first (it pins the
branch), then capture a fixture (it pins the user-visible outcome). If you're
fixing a parser bug: write the failing synthesised test from the malformed
bytes, fix the production code, then add the offending save to `Fixtures/` to
prevent the regression coming back.

## v1.1 ↔ v1.2 compatibility canary

The library has to keep parsing the stable v1.1 branch saves while the
experimental v1.2 branch evolves. Since we have no v1.1 `.sav` fixture (would
need a Satisfactory branch switch to capture), the v1.1 surface is locked in by
**synthesised tests with `saveVersion < 53`** rather than a real fixture:

- `PropertySerializerLegacyTests` — every legacy `Deserialize<Type>Property`
  branch (Bool, Int, Int64, UInt32, UInt64, Float, Double, Int8, Byte both
  modes, Name, Str, Object, SoftObject, Enum, Array of {Int, Str, Object, Bool,
  Float, Double, Int64, Enum, Interface}, Text history-types 0 and 11) at
  `saveVersion = 50`.
- `ExtraDataSerializerLegacyTests` — Conveyor / PowerLine (incl. the
  v33-40 cached-translation window) / Circuit / Vehicle (both cargo-block
  sizes) / Locomotive / Blueprint / PlayerData (mode 248) / UnknownExtraData
  fallback / DroneStation / ConveyorChainActor / LightweightBuildableSubsystem
  empty shapes.
- `Compat/VersionCompatibilityTests` — pins the *dispatch* logic at the v1.1↔v1.2
  seam: legacy switch vs RawProperty at `saveVersion ∈ {52, 53}`, ObjectHeader
  Flags gate at 50/51, TypedData Quat float/double gate at 40/41, CircuitData
  leading-count gate at 52/53, ConveyorData payload gate at 53.

If a v1.2 commit silently changes how the parser handles older saves, the
**first failure will be in `VersionCompatibilityTests` or the legacy switch
tests** — not after a customer reports a parse error against a v1.1 save.

You can prove the canary works by running the deliberate-break sanity check —
flip any `saveVersion >= 53` check in `PropertySerializer.cs` to `> 53` and at
least one test fails immediately.

## Current coverage baseline

Run with `dotnet test --collect:"XPlat Code Coverage" --settings coverlet.runsettings.xml`
and inspect the OpenCover XML. As of 2026-05-12, on the `SatisfactorySaveNet`
implementation assembly:

| File | Sequence | Branch |
|---|---:|---:|
| `FSaveObjectVersionDataSerializer.cs` | 100% | 100% |
| `HeaderSerializer.cs` | 100% | 100% |
| `ChunkSerializer.cs`, `HexSerializer.cs`, `StringSerializer.cs` | 100% | 100% |
| `ObjectReferenceSerializer.cs`, `SoftObjectReferenceSerializer.cs` | 100% | 100% |
| `ObjectHeaderSerializer.cs` | 100% | 100% |
| `KnownConstants.cs` | 100% | 93% |
| `ObjectSerializer.cs` | 93% | 86% |
| `SaveFileSerializer.cs` | 88% | 71% |
| `BodySerializer.cs` | 81% | 76% |
| `VectorSerializer.cs` | 73% | 50% |
| `PropertySerializer.cs` | 63% | 43% |
| `ExtraDataSerializer.cs` | 52% | 56% |
| `TypedDataSerializer.cs` | 34% | 17% |
| **Module aggregate** | **~63%** | **~38%** |

The 63% aggregate is below an 80% target because `TypedDataSerializer` is a
fan-out dispatch over ~30 FicsItNetworks/Lua/FIR struct-types whose wire
formats are not consistently documented. Adding synthesised tests for every
one would be coverage chasing — most of them are best exercised by a real
fixture that uses the corresponding mod, not by hand-built byte sequences.
Treat 80% as aspirational; prioritise meaningful tests over chasing the number.
