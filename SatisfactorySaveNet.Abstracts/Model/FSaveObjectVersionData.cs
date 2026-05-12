using System;
using System.Collections.Generic;

namespace SatisfactorySaveNet.Abstracts.Model;

/// <summary>
/// Per-object or per-level save versioning struct introduced at
/// <c>SaveCustomVersion.SerializeDataPackageVersionAndCustomVersions</c> (custom version 53).
/// Mirrors the Unreal <c>FSaveObjectVersionData</c> binary layout.
/// </summary>
public class FSaveObjectVersionData
{
    public uint SaveObjectVersionDataVersion { get; set; }
    public required FPackageFileVersion PackageFileVersion { get; set; }
    public int LicenceVersion { get; set; }
    public required FEngineVersion EngineVersion { get; set; }
    public required FCustomVersionContainer CustomVersionContainer { get; set; }
}

public class FPackageFileVersion
{
    public int Ue4Version { get; set; }
    public int Ue5Version { get; set; }
}

public class FEngineVersion
{
    public ushort Major { get; set; }
    public ushort Minor { get; set; }
    public ushort Patch { get; set; }
    public uint Changelist { get; set; }
    public string Branch { get; set; } = string.Empty;
}

public class FCustomVersionContainer
{
    public ICollection<FCustomVersion> Versions { get; set; } = [];
}

public class FCustomVersion
{
    public Guid Guid { get; set; }
    public int Version { get; set; }
}
