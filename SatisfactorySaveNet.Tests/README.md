# SatisfactorySaveNet.Tests

NUnit + FluentAssertions + NSubstitute. Two test layers.

## Layer 1 — synthesised binary (`Serializers/`)

Pure unit tests that build a `BinaryReader` over a known byte sequence and assert
the serializer produces the expected model. Fast, deterministic, no `.sav` file
dependency. Focused on the v1.2 code paths:

- `FSaveObjectVersionDataSerializerTests` — the new `FSaveObjectVersionData`
  serializer (UE4/UE5 versions, engine version, custom-version array).
- `PropertySerializerV12Tests` — the `serializationControl` byte, complete-tag
  flow, flag bits for index / GUID, BoolProperty flag-bit encoding, binary-size
  fence.
- `ObjectSerializerV12Tests` — `ReadOptionalObjectGuid` and
  `ReadOptionalPostBodyVersionData` (both pre-v1.2 no-op and v1.2 active paths).
- `SaveFileSerializerV12Tests` — empty-stream guard and the pre-v21 path
  bypassing the FSaveObjectVersionData hook. The full SaveVersion ≥ 53
  compressed path is covered by fixtures, not synthesised.

`Serializers/BinaryReaderHelpers.cs` is the only shared utility — `MakeReader`,
`BuildBytes`, `WriteFString`, `WriteGuid`.

## Layer 2 — real `.sav` fixtures (`Fixtures/`)

`RealSaveFixtureTests` deserializes every `Fixtures/*.sav` file at runtime via
`[TestCaseSource]`. The directory is copied to the test output via
`CopyToOutputDirectory=PreserveNewest` in the csproj. When no fixtures are
present, the suite passes vacuously.

**Naming convention** — `SatisfactorySaveNet - v<game-version> - <Scenario>.sav`:

- `SatisfactorySaveNet - v1.2 - Empty World.sav` — pristine world, nothing built
- `SatisfactorySaveNet - v1.2 - The Hub.sav` — only The Hub placed
- (add more as needed)

Game version is what humans recognise; the on-disk `SaveCustomVersion` (53, 60, …)
is asserted from within the test rather than encoded in the filename.

For each named fixture, add a dedicated `[Test]` method with hand-recorded
invariants (`SaveVersion`, `MapName`, body type, key actor counts). Keep
fixtures small — no Git LFS. If a save is large, simplify it in-game first.

Personal autosaves dropped into `Fixtures/` for local debugging are gitignored
automatically — only files matching `SatisfactorySaveNet - *.sav` are tracked.
The smoke test still exercises whatever's in the directory locally.

## Running

```powershell
dotnet test vendor\SatisfactorySaveNet\SatisfactorySaveNet.sln
```

## Coverage

```powershell
dotnet test vendor\SatisfactorySaveNet\SatisfactorySaveNet.sln `
  --collect:"XPlat Code Coverage" `
  --settings vendor\SatisfactorySaveNet\coverlet.runsettings.xml
```

Report lands in `TestResults/<guid>/coverage.opencover.xml` (OpenCover XML).
