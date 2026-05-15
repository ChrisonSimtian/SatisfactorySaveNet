using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SatisfactorySaveNet.Abstracts;
using SatisfactorySaveNet.Abstracts.Exceptions;
using SatisfactorySaveNet.Abstracts.Model;
using System.IO;
using System.Linq;

namespace SatisfactorySaveNet;

public class ObjectSerializer : IObjectSerializer
{
    /// <summary>
    /// SaveCustomVersion at which a per-object FSaveObjectVersionData block is serialized
    /// after the body — <c>SerializeDataPackageVersionAndCustomVersions</c>.
    /// </summary>
    private const int SerializeDataPackageVersionAndCustomVersions = 53;

    public static readonly IObjectSerializer Instance = new ObjectSerializer(NullLoggerFactory.Instance, StringSerializer.Instance, ObjectReferenceSerializer.Instance, PropertySerializer.Instance, ExtraDataSerializer.Instance, HexSerializer.Instance, FSaveObjectVersionDataSerializer.Instance);

    private readonly IStringSerializer _stringSerializer;
    private readonly IObjectReferenceSerializer _objectReferenceSerializer;
    private readonly IPropertySerializer _propertySerializer;
    private readonly IExtraDataSerializer _extraDataSerializer;
    private readonly IHexSerializer _hexSerializer;
    private readonly IFSaveObjectVersionDataSerializer _objectVersionDataSerializer;
    private readonly ILogger<ObjectSerializer> _logger;

    public ObjectSerializer(ILoggerFactory loggerFactory, IStringSerializer stringSerializer, IObjectReferenceSerializer objectReferenceSerializer, IPropertySerializer propertySerializer, IExtraDataSerializer extraDataSerializer, IHexSerializer hexSerializer, IFSaveObjectVersionDataSerializer objectVersionDataSerializer)
    {
        _stringSerializer = stringSerializer;
        _objectReferenceSerializer = objectReferenceSerializer;
        _propertySerializer = propertySerializer;
        _extraDataSerializer = extraDataSerializer;
        _hexSerializer = hexSerializer;
        _objectVersionDataSerializer = objectVersionDataSerializer;
        _logger = loggerFactory.CreateLogger<ObjectSerializer>();
    }

    public ComponentObject Deserialize(BinaryReader reader, Header header, ComponentObject componentObject, int? saveVersion = null)
    {
        return componentObject switch
        {
            ActorObject actorObject => DeserializeActor(reader, header, actorObject),
            ComponentObject => DeserializeComponent(reader, header, componentObject),
            _ => throw new CorruptedSatisFactorySaveFileException("Encountered unknown object type")
        };
    }

    /// <summary>
    /// Reads the optional post-body FSaveObjectVersionData block introduced at
    /// SaveCustomVersion 53. The threshold is the <em>per-object</em> saveCustomVersion
    /// (the first Int32 of each object record at header.SaveVersion >= 41), not the
    /// level or header version.
    /// </summary>
    internal void ReadOptionalPostBodyVersionData(BinaryReader reader, ComponentObject obj, int objectSaveVersion)
    {
        if (objectSaveVersion < SerializeDataPackageVersionAndCustomVersions)
            return;
        var shouldSerialize = reader.ReadInt32() == 1;
        if (shouldSerialize)
            obj.ObjectVersionData = _objectVersionDataSerializer.Deserialize(reader);
    }

    /// <summary>
    /// At v1.2+, the property list is followed by an Int32 flag and optional 16-byte GUID
    /// before any class-specific extra data. We consume but don't yet expose either value.
    /// </summary>
    internal static void ReadOptionalObjectGuid(BinaryReader reader, int objectSaveVersion)
    {
        if (objectSaveVersion < SerializeDataPackageVersionAndCustomVersions)
            return;
        var hasGuid = reader.ReadInt32() > 0;
        if (hasGuid)
            _ = reader.ReadBytes(16);
    }

    private ActorObject DeserializeActor(BinaryReader reader, Header header, ActorObject actorObject)
    {
        var perObjectSaveVersion = header.SaveVersion;
        if (header.SaveVersion >= 41)
        {
            var version = reader.ReadInt32();
            perObjectSaveVersion = version;
            if (version != header.SaveVersion)
                actorObject.EntitySaveVersion = version;
            _ = reader.ReadInt32();
        }
        var binarySize = reader.ReadInt32();
        var positionStart = reader.BaseStream.Position;

        var parentObjectRoot = _stringSerializer.Deserialize(reader);

        var parentObjectName = _stringSerializer.Deserialize(reader);

        var componentsCount = reader.ReadInt32();
        var components = new ObjectReference[componentsCount];

        for (var i = 0; i < componentsCount; i++)
        {
            var objectRef = _objectReferenceSerializer.Deserialize(reader);
            components[i] = objectRef;
        }

        actorObject.ParentObjectRoot = parentObjectRoot;
        actorObject.ParentObjectName = parentObjectName;

        var expectedPosition = positionStart + binarySize;
        actorObject.Components = components;

        if (expectedPosition == reader.BaseStream.Position)
        {
            ReadOptionalPostBodyVersionData(reader, actorObject, perObjectSaveVersion);
            return actorObject;
        }

        var properties = _propertySerializer.DeserializeProperties(reader, header, expectedPosition: expectedPosition, saveVersion: perObjectSaveVersion).ToArray();

        actorObject.Properties = properties;

        // At v1.2+, the property list is followed by an Int32 hasGuid flag + optional GUID
        // before any class-specific ExtraData. Mirrors SaveObject.ParseData in etothepii.
        ReadOptionalObjectGuid(reader, perObjectSaveVersion);

        // v1.2+: most class-specific ExtraData layouts diverged from pre-1.2. We run the
        // ExtraData parser for the classes whose v1.2 format we've ported (Conveyor belts
        // + lifts, PowerLine, CircuitSubsystem); for everything else we skip and let the
        // missing-bytes handler below absorb the remainder.
        var v12 = perObjectSaveVersion >= SerializeDataPackageVersionAndCustomVersions;
        // FGConveyorChainActor's ExtraData blob has the same wire format pre- and
        // post-v1.2 (AnthorNet/SC-InteractiveMap's Read.js has no version branching
        // for it), so we let the existing DeserializeConveyorChainActor handle both.
        // Without this entry, v1.2 chain actors silently fall through to the missing-
        // bytes absorber and consumers see empty ConveyorActors[].
        var extraDataPortedAtV12 = KnownConstants.IsConveyor(actorObject.TypePath)
                                || KnownConstants.IsConveyorActor(actorObject.TypePath)
                                || KnownConstants.IsPowerLine(actorObject.TypePath)
                                || actorObject.TypePath == "/Game/FactoryGame/-Shared/Blueprint/BP_CircuitSubsystem.BP_CircuitSubsystem_C";
        if (!v12 || extraDataPortedAtV12)
            actorObject.ExtraData = _extraDataSerializer.Deserialize(reader, actorObject.TypePath, header, expectedPosition);

        var missingBytes = expectedPosition - reader.BaseStream.Position;

        if (missingBytes > 0)
        {
            var hex = _hexSerializer.Deserialize(reader, missingBytes.ToInt());
            if (hex.Any(c => c != '\0'))
                _logger.LogWarning("Missing bytes: {MissingBytes}", hex);
        }
        else if (missingBytes < 0)
            reader.BaseStream.Seek(missingBytes, SeekOrigin.Current);

        ReadOptionalPostBodyVersionData(reader, actorObject, perObjectSaveVersion);
        return actorObject;
    }

    private ComponentObject DeserializeComponent(BinaryReader reader, Header header, ComponentObject componentObject)
    {
        var perObjectSaveVersion = header.SaveVersion;
        if (header.SaveVersion >= 41)
        {
            var version = reader.ReadInt32();
            perObjectSaveVersion = version;
            if (version != header.SaveVersion)
                componentObject.EntitySaveVersion = version;
            _ = reader.ReadInt32();
        }
        var binarySize = reader.ReadInt32();
        var positionStart = reader.BaseStream.Position;

        var properties = _propertySerializer.DeserializeProperties(reader, header, saveVersion: perObjectSaveVersion).ToArray();
        componentObject.Properties = properties;

        // At v1.2+, hasGuid + optional GUID between properties and class-specific ExtraData.
        ReadOptionalObjectGuid(reader, perObjectSaveVersion);

        var expectedPosition = positionStart + binarySize;
        var missingBytes = expectedPosition - reader.BaseStream.Position;

        if (missingBytes > 0)
        {
            var hex = _hexSerializer.Deserialize(reader, missingBytes.ToInt());
            if (hex.Any(c => c != '\0'))
                _logger.LogCritical("BAD READ {MissingBytes}", missingBytes);
        }
        else if (missingBytes < 0)
            reader.BaseStream.Seek(missingBytes, SeekOrigin.Current);

        ReadOptionalPostBodyVersionData(reader, componentObject, perObjectSaveVersion);
        return componentObject;
    }
}