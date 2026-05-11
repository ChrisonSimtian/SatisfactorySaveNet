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
    private void ReadOptionalPostBodyVersionData(BinaryReader reader, ComponentObject obj, int objectSaveVersion)
    {
        if (objectSaveVersion < SerializeDataPackageVersionAndCustomVersions)
            return;
        var shouldSerialize = reader.ReadInt32() == 1;
        if (shouldSerialize)
            obj.ObjectVersionData = _objectVersionDataSerializer.Deserialize(reader);
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

        var properties = _propertySerializer.DeserializeProperties(reader, header, expectedPosition: expectedPosition).ToArray();

        actorObject.Properties = properties;
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

        var properties = _propertySerializer.DeserializeProperties(reader, header).ToArray();
        componentObject.Properties = properties;

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