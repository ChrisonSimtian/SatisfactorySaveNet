using SatisfactorySaveNet.Abstracts;
using SatisfactorySaveNet.Abstracts.Model;
using System;
using System.IO;

namespace SatisfactorySaveNet;

/// <summary>
/// Reads the Unreal <c>FSaveObjectVersionData</c> struct that follows certain object bodies and
/// non-persistent levels at <c>SaveCustomVersion.SerializeDataPackageVersionAndCustomVersions</c> (53).
/// Mirrors <c>FSaveObjectVersionData.read</c> in <c>@etothepii/satisfactory-file-parser</c>.
/// </summary>
public class FSaveObjectVersionDataSerializer : IFSaveObjectVersionDataSerializer
{
    public static readonly IFSaveObjectVersionDataSerializer Instance = new FSaveObjectVersionDataSerializer(StringSerializer.Instance);

    private readonly IStringSerializer _stringSerializer;

    public FSaveObjectVersionDataSerializer(IStringSerializer stringSerializer)
    {
        _stringSerializer = stringSerializer;
    }

    public FSaveObjectVersionData Deserialize(BinaryReader reader)
    {
        var saveObjectVersionDataVersion = reader.ReadUInt32();
        var packageFileVersion = new FPackageFileVersion
        {
            Ue4Version = reader.ReadInt32(),
            Ue5Version = reader.ReadInt32()
        };
        var licenceVersion = reader.ReadInt32();

        var engineVersion = new FEngineVersion
        {
            Major = reader.ReadUInt16(),
            Minor = reader.ReadUInt16(),
            Patch = reader.ReadUInt16(),
            Changelist = reader.ReadUInt32(),
            Branch = _stringSerializer.Deserialize(reader)
        };

        var count = reader.ReadInt32();
        var versions = new FCustomVersion[count];
        for (var i = 0; i < count; i++)
        {
            // GUID is 4 × UInt32 = 16 bytes. Build via byte buffer to match Unreal's layout.
            var bytes = reader.ReadBytes(16);
            var version = reader.ReadInt32();
            versions[i] = new FCustomVersion
            {
                Guid = new Guid(bytes),
                Version = version
            };
        }

        return new FSaveObjectVersionData
        {
            SaveObjectVersionDataVersion = saveObjectVersionDataVersion,
            PackageFileVersion = packageFileVersion,
            LicenceVersion = licenceVersion,
            EngineVersion = engineVersion,
            CustomVersionContainer = new FCustomVersionContainer { Versions = versions }
        };
    }
}
