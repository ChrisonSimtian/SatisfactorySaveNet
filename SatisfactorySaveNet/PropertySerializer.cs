using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SatisfactorySaveNet.Abstracts;
using SatisfactorySaveNet.Abstracts.Model;
using SatisfactorySaveNet.Abstracts.Model.Properties;
using SatisfactorySaveNet.Abstracts.Model.Typed;
using SatisfactorySaveNet.Abstracts.Model.Union;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

namespace SatisfactorySaveNet;

public class PropertySerializer : IPropertySerializer
{
    public static readonly IPropertySerializer Instance = new PropertySerializer(StringSerializer.Instance, ObjectReferenceSerializer.Instance, VectorSerializer.Instance, HexSerializer.Instance, SoftObjectReferenceSerializer.Instance, NullLoggerFactory.Instance);

    private readonly IStringSerializer _stringSerializer;
    private readonly IObjectReferenceSerializer _objectReferenceSerializer;
    private readonly ISoftObjectReferenceSerializer _softObjectReferenceSerializer;
    private readonly ITypedDataSerializer _typedDataSerializer;
    private readonly IHexSerializer _hexSerializer;
    private readonly IVectorSerializer _vectorSerializer;
    private readonly ILogger _logger;

    internal PropertySerializer(IStringSerializer stringSerializer, IObjectReferenceSerializer objectReferenceSerializer, ITypedDataSerializer typedDataSerializer, IHexSerializer hexSerializer, IVectorSerializer vectorSerializer, ISoftObjectReferenceSerializer softObjectReferenceSerializer, ILoggerFactory loggerFactory)
    {
        _stringSerializer = stringSerializer;
        _objectReferenceSerializer = objectReferenceSerializer;
        _typedDataSerializer = typedDataSerializer;
        _hexSerializer = hexSerializer;
        _vectorSerializer = vectorSerializer;
        _softObjectReferenceSerializer = softObjectReferenceSerializer;
        _logger = loggerFactory.CreateLogger<PropertySerializer>() ?? NullLogger<PropertySerializer>.Instance;
    }

    public PropertySerializer(IStringSerializer stringSerializer, IObjectReferenceSerializer objectReferenceSerializer, IVectorSerializer vectorSerializer, IHexSerializer hexSerializer, ISoftObjectReferenceSerializer softObjectReferenceSerializer, ILoggerFactory loggerFactory)
    {
        _stringSerializer = stringSerializer;
        _objectReferenceSerializer = objectReferenceSerializer;
        _typedDataSerializer = new TypedDataSerializer(vectorSerializer, stringSerializer, this, hexSerializer, objectReferenceSerializer);
        _hexSerializer = hexSerializer;
        _vectorSerializer = vectorSerializer;
        _softObjectReferenceSerializer = softObjectReferenceSerializer;
        _logger = loggerFactory.CreateLogger<PropertySerializer>() ?? NullLogger<PropertySerializer>.Instance;
    }

    /// <summary>
    /// SaveCustomVersion at which the FPropertyTag adopts its complete-type-name format
    /// (and SaveObject.ParseData prefixes the property list with a serializationControl byte).
    /// </summary>
    private const int SerializeDataPackageVersionAndCustomVersions = 53;

    public IEnumerable<Property> DeserializeProperties(BinaryReader reader, Header? header = null, string? type = null, long? expectedPosition = null, int? saveVersion = null)
    {
        // At v1.2+, a one-byte serializationControl precedes the property list — mirrors
        // SaveObject.ParseData in etothepii. Read and discard.
        if (saveVersion >= SerializeDataPackageVersionAndCustomVersions && type == null)
            _ = reader.ReadByte();

        if (expectedPosition != null && expectedPosition < reader.BaseStream.Position)
            yield break;

        Property? property;
        while ((property = DeserializeProperty(reader, header, type, saveVersion)) != null)
        {
            yield return property;

            if (expectedPosition != null && expectedPosition < reader.BaseStream.Position)
                yield break;
        }
    }

    [return: NotNullIfNotNull(nameof(type))]
    public Property? DeserializeProperty(BinaryReader reader, Header? header = null, string? type = null, int? saveVersion = null)
    {
        var propertyName = string.Empty;

        if (type == null)
        {
            propertyName = _stringSerializer.Deserialize(reader);

            if (string.Equals(propertyName, "None", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(propertyName))
                return null;

            // v1.2+ complete-tag format. Read the full tag, then either deep-parse the
            // value (for the handful of property types the planner cares about — Object
            // refs, arrays of Object refs, and strings) or skip the value bytes.
            // The stream stays aligned either way.
            if (saveVersion >= SerializeDataPackageVersionAndCustomVersions)
            {
                var tagNode = ReadPropertyTagNode(reader);
                var binarySize = reader.ReadInt32();
                var flags = reader.ReadByte();
                var index = (flags & 0x1) != 0 ? reader.ReadInt32() : 0;
                System.Guid? propertyGuid = null;
                if ((flags & 0x2) != 0)
                {
                    var guidBytes = reader.ReadBytes(16);
                    propertyGuid = new System.Guid(guidBytes);
                }

                var posBeforeValue = reader.BaseStream.Position;
                var raw = new RawProperty
                {
                    Name = propertyName,
                    Index = index,
                    Type = tagNode.Name,
                    TypeNode = tagNode,
                    BinarySize = binarySize,
                    Flags = flags,
                    PropertyGuid = propertyGuid
                };

                if (binarySize > 0)
                    TryParseKnownValue(reader, raw, binarySize);

                // Always end up at posBeforeValue + binarySize regardless of how (or
                // whether) the value parsed. Catches read-too-few and read-too-many bugs
                // in the targeted parsers without throwing.
                var expected = posBeforeValue + binarySize;
                var diff = expected - reader.BaseStream.Position;
                if (diff > 0)
                    reader.BaseStream.Seek(diff, SeekOrigin.Current);
                else if (diff < 0)
                    reader.BaseStream.Seek(diff, SeekOrigin.Current);

                return raw;
            }

            type = _stringSerializer.Deserialize(reader);
        }

        Property property = type switch
        {
            nameof(ArrayProperty) => header == null ? throw new ArgumentNullException(nameof(header)) : DeserializeArrayProperty(reader, header),
            nameof(BoolProperty) => DeserializeBoolProperty(reader),
            nameof(ByteProperty) => DeserializeByteProperty(reader),
            nameof(EnumProperty) => DeserializeEnumProperty(reader),
            nameof(FloatProperty) => DeserializeFloatProperty(reader),
            nameof(IntProperty) => DeserializeIntProperty(reader),
            nameof(Int64Property) => DeserializeInt64Property(reader),
            nameof(UInt64Property) => DeserializeUInt64Property(reader),
            nameof(MapProperty) => header == null ? throw new ArgumentNullException(nameof(header)) : DeserializeMapProperty(reader, header, propertyName, type),
            nameof(NameProperty) => DeserializeNameProperty(reader),
            nameof(ObjectProperty) => DeserializeObjectProperty(reader),
            nameof(SoftObjectProperty) => DeserializeSoftObjectProperty(reader),
            nameof(SetProperty) => DeserializeSetProperty(reader, propertyName),
            nameof(StrProperty) => DeserializeStrProperty(reader),
            nameof(StructProperty) => header == null ? throw new ArgumentNullException(nameof(header)) : DeserializeStructProperty(reader, header),
            nameof(TextProperty) => header == null ? throw new ArgumentNullException(nameof(header)) : DeserializeTextProperty(reader, header),
            nameof(UInt32Property) => DeserializeUInt32Property(reader),
            nameof(Int8Property) => DeserializeInt8Property(reader),
            nameof(DoubleProperty) => DeserializeDoubleProperty(reader),
            nameof(FINNetworkProperty) => DeserializeFINNetworkProperty(reader),

            _ => throw new ArgumentOutOfRangeException(nameof(type), type, $"Unknown property type '{type}' (propertyName='{propertyName}', streamPos={reader.BaseStream.Position})")
        };

        property.Name = propertyName;
        return property;
    }

    /// <summary>
    /// Reads the recursive FPropertyTagNode struct — a property's type name plus any
    /// nested type info (e.g. ArrayProperty's element type, StructProperty's struct name).
    /// Mirrors etothepii's FPropertyTagNode.read.
    /// </summary>
    private FPropertyTagNode ReadPropertyTagNode(BinaryReader reader)
    {
        var name = _stringSerializer.Deserialize(reader);
        var count = reader.ReadInt32();
        var children = new FPropertyTagNode[count];
        for (var i = 0; i < count; i++)
            children[i] = ReadPropertyTagNode(reader);
        return new FPropertyTagNode { Name = name, Children = children };
    }

    /// <summary>
    /// Targeted value parser for the v1.2 RawProperty path. Reads the value bytes when
    /// the type is one we care about for the planner (object refs, arrays of object
    /// refs, strings); otherwise leaves the reader untouched and relies on the caller
    /// to skip <paramref name="binarySize"/> bytes.
    /// </summary>
    private void TryParseKnownValue(BinaryReader reader, RawProperty raw, int binarySize)
    {
        switch (raw.Type)
        {
            case "ObjectProperty":
                raw.ObjectValue = ReadObjectRef(reader);
                break;

            case "StrProperty":
            case "NameProperty":
                raw.StringValue = _stringSerializer.Deserialize(reader);
                break;

            case "ArrayProperty" when raw.TypeNode is { Children.Count: > 0 }
                                  && IsObjectRefChild(raw.TypeNode.Children):
            {
                var elementCount = reader.ReadInt32();
                if (elementCount < 0 || elementCount > 1_000_000)
                    return; // refuse to allocate insane arrays — keep stream aligned via caller
                var values = new ObjectReferenceValue[elementCount];
                for (var i = 0; i < elementCount; i++)
                    values[i] = ReadObjectRef(reader);
                raw.ArrayObjectValues = values;
                break;
            }

            default:
                // Unknown / unparsed — caller seeks past `binarySize` bytes.
                break;
        }
    }

    private ObjectReferenceValue ReadObjectRef(BinaryReader reader)
    {
        var levelName = _stringSerializer.Deserialize(reader);
        var pathName = _stringSerializer.Deserialize(reader);
        return new ObjectReferenceValue(levelName, pathName);
    }

    private static bool IsObjectRefChild(ICollection<FPropertyTagNode> children)
    {
        foreach (var c in children)
            return c.Name == "ObjectProperty";
        return false;
    }

    private ArrayPropertyBase DeserializeArrayProperty(BinaryReader reader, Header header, string type, int count)
    {
        return type switch
        {
            nameof(ByteProperty) => DeserializeArrayByteProperty(reader, count, type),
            nameof(BoolProperty) => DeserializeBoolArrayProperty(reader, count),
            nameof(IntProperty) => DeserializeArrayIntProperty(reader, count),
            nameof(Int64Property) => DeserializeArrayInt64Property(reader, count),
            nameof(UInt64Property) => DeserializeArrayUInt64Property(reader, count),
            nameof(DoubleProperty) => DeserializeArrayDoubleProperty(reader, count),
            nameof(FloatProperty) => DeserializeArrayFloatProperty(reader, count),
            nameof(EnumProperty) => DeserializeArrayEnumProperty(reader, count),
            nameof(StrProperty) => DeserializeArrayStrProperty(reader, count),
            nameof(TextProperty) => DeserializeArrayTextProperty(reader, header, count),
            nameof(SoftObjectProperty) => DeserializeArraySoftObjectProperty(reader, count),
            nameof(ObjectProperty) => DeserializeArrayObjectProperty(reader, count),
            nameof(InterfaceProperty) => DeserializeArrayInterfaceProperty(reader, count),
            nameof(StructProperty) => DeserializeArrayStructProperty(reader, header, count),
            //ToDo: All implemented?

            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }

    private ArrayTextProperty DeserializeArrayTextProperty(BinaryReader reader, Header header, int count)
    {
        var values = new TextProperty[count];

        for (var x = 0; x < count; x++)
        {
            values[x] = DeserializeTextProperty(reader, header);
        }

        return new ArrayTextProperty
        {
            Values = values
        };
    }

    private ArrayStrProperty DeserializeArrayStrProperty(BinaryReader reader, int count)
    {
        var values = new string[count];

        for (var x = 0; x < count; x++)
        {
            values[x] = _stringSerializer.Deserialize(reader);
        }

        return new ArrayStrProperty
        {
            Values = values
        };
    }

    private ArrayEnumProperty DeserializeArrayEnumProperty(BinaryReader reader, int count)
    {
        var values = new string[count];

        for (var x = 0; x < count; x++)
        {
            values[x] = _stringSerializer.Deserialize(reader);
        }

        return new ArrayEnumProperty
        {
            Values = values
        };
    }

    private static ArrayFloatProperty DeserializeArrayFloatProperty(BinaryReader reader, int count)
    {
        var values = new float[count];

        for (var x = 0; x < count; x++)
        {
            values[x] = reader.ReadSingle();
        }

        return new ArrayFloatProperty
        {
            Values = values
        };
    }

    private static ArrayInt64Property DeserializeArrayInt64Property(BinaryReader reader, int count)
    {
        var values = new long[count];

        for (var x = 0; x < count; x++)
        {
            values[x] = reader.ReadInt64();
        }

        return new ArrayInt64Property
        {
            Values = values
        };
    }

    private static ArrayUInt64Property DeserializeArrayUInt64Property(BinaryReader reader, int count)
    {
        var values = new ulong[count];

        for (var x = 0; x < count; x++)
        {
            values[x] = reader.ReadUInt64();
        }

        return new ArrayUInt64Property
        {
            Values = values
        };
    }

    private static ArrayDoubleProperty DeserializeArrayDoubleProperty(BinaryReader reader, int count)
    {
        var values = new double[count];

        for (var x = 0; x < count; x++)
        {
            values[x] = reader.ReadDouble();
        }

        return new ArrayDoubleProperty
        {
            Values = values
        };
    }

    private static ArrayIntProperty DeserializeArrayIntProperty(BinaryReader reader, int count)
    {
        var values = new int[count];

        for (var x = 0; x < count; x++)
        {
            values[x] = reader.ReadInt32();
        }

        return new ArrayIntProperty
        {
            Values = values
        };
    }

    private ArrayInterfaceProperty DeserializeArrayInterfaceProperty(BinaryReader reader, int count)
    {
        var values = new ObjectReference[count];

        for (var x = 0; x < count; x++)
        {
            values[x] = _objectReferenceSerializer.Deserialize(reader);
        }

        return new ArrayInterfaceProperty
        {
            Values = values
        };
    }

    private static ArrayBoolProperty DeserializeBoolArrayProperty(BinaryReader reader, int count)
    {
        var values = new sbyte[count];

        for (var x = 0; x < count; x++)
        {
            values[x] = reader.ReadSByte();
        }

        return new ArrayBoolProperty
        {
            Values = values
        };
    }

    private static ArrayByteProperty DeserializeArrayByteProperty(BinaryReader reader, int count, string propertyName)
    {
        sbyte[] values;

        if (propertyName.Equals("mFogOfWarRawData", StringComparison.Ordinal))
        {
            var newCount = count / 4;
            values = new sbyte[newCount];
            for (var x = 0; x < newCount; x++)
            {
                var unknown1 = reader.ReadSByte();
                var unknown2 = reader.ReadSByte();
                values[x] = reader.ReadSByte();
                var unknown3 = reader.ReadSByte();
            }
        }
        else
        {
            values = new sbyte[count];
            for (var x = 0; x < count; x++)
            {
                values[x] = reader.ReadSByte();
            }
        }

        return new ArrayByteProperty
        {
            Values = values
        };
    }

    private ArrayObjectProperty DeserializeArrayObjectProperty(BinaryReader reader, int count)
    {
        var values = new ObjectReference[count];

        for (var x = 0; x < count; x++)
        {
            values[x] = _objectReferenceSerializer.Deserialize(reader);
        }

        return new ArrayObjectProperty
        {
            Values = values
        };
    }

    private ArraySoftObjectProperty DeserializeArraySoftObjectProperty(BinaryReader reader, int count)
    {
        var values = new SoftObjectReference[count];

        for (var x = 0; x < count; x++)
        {
            values[x] = _softObjectReferenceSerializer.Deserialize(reader);
        }

        return new ArraySoftObjectProperty
        {
            Values = values
        };
    }

    private ArrayProperty DeserializeArrayProperty(BinaryReader reader, Header header)
    {
        var binarySize = reader.ReadInt32();
        var index = reader.ReadInt32();
        var type = _stringSerializer.Deserialize(reader);
        _ = reader.ReadSByte();
        var length = reader.ReadInt32();

        var property = DeserializeArrayProperty(reader, header, type, length);

        return new ArrayProperty
        {
            Index = index,
            Type = type,
            Property = property
        };
    }

    private TextProperty DeserializeTextProperty(BinaryReader reader, Header header, int? binarySize = null, int? index = null, sbyte? padding = null)
    {
        binarySize ??= reader.ReadInt32();
        index ??= reader.ReadInt32();
        padding ??= reader.ReadSByte();
        var flags = reader.ReadInt32();
        var historyType = reader.ReadByte();

        string? nameSpace = null;
        string? key = null;
        string? value = null;
        TextProperty? sourceFmt = null;
        TextProperty? sourceText = null;
        byte? transformType = null;
        string? tableId = null;
        string? textKey = null;
        int? isCultureInvariant = null;
        TextArgument[]? arguments = null;

        switch (historyType)
        {
            case 0:
                nameSpace = _stringSerializer.Deserialize(reader);
                key = _stringSerializer.Deserialize(reader);
                value = _stringSerializer.Deserialize(reader);
                break;
            case 1:
            case 3:
                sourceFmt = DeserializeTextProperty(reader, header, binarySize, index, padding);
                var argumentsCount = reader.ReadInt32();
                arguments = new TextArgument[argumentsCount];

                for (var x = 0; x < argumentsCount; x++)
                {
                    var name = _stringSerializer.Deserialize(reader);
                    var valueType = reader.ReadByte();
                    TextArgument textArgument = valueType switch
                    {
                        0 => new TextArgumentV0 { Name = name, ArgumentValue = reader.ReadInt32(), ArgumentValueUnknown = reader.ReadInt32() },
                        4 => new TextArgumentV4 { Name = name, ArgumentPropertyValue = DeserializeTextProperty(reader, header, binarySize, index, padding) },
                        _ => throw new InvalidDataException("Unknown valueType type in array text property"),
                    };
                    arguments[x] = textArgument;
                }
                break;
            case 10:
                sourceText = DeserializeTextProperty(reader, header, binarySize, index, padding);
                transformType = reader.ReadByte();
                break;
            case 11:
                tableId = _stringSerializer.Deserialize(reader);
                textKey = _stringSerializer.Deserialize(reader);
                break;
            case 255:
                if (header.BuildVersion < 140822)
                    break;
                isCultureInvariant = reader.ReadInt32();
                if (isCultureInvariant != 1)
                    break;
                value = _stringSerializer.Deserialize(reader);
                break;
            default:
                throw new InvalidDataException("Unknown history type in array text property");
        }

        return new TextProperty
        {
            Index = index.Value,
            Flags = flags,
            HistoryType = historyType,
            IsCultureInvariant = isCultureInvariant,
            Value = value,
            NameSpace = nameSpace,
            Key = key,
            SourceFmt = sourceFmt,
            SourceText = sourceText,
            TransformType = transformType,
            TableId = tableId,
            TextKey = textKey,
            Arguments = arguments
        };
    }

    private ArrayStructProperty DeserializeArrayStructProperty(BinaryReader reader, Header header, int length)
    {
        var propertyName = _stringSerializer.Deserialize(reader);
        var propertyType = _stringSerializer.Deserialize(reader);

        var binarySize = reader.ReadInt32();
        _ = reader.ReadInt32();
        var elementType = _stringSerializer.Deserialize(reader);

        var uuid1 = reader.ReadInt32();
        var uuid2 = reader.ReadInt32();
        var uuid3 = reader.ReadInt32();
        var uuid4 = reader.ReadInt32();
        _ = reader.ReadSByte();

        var values = new TypedData[length];

        for (var x = 0; x < length; x++)
        {
            values[x] = _typedDataSerializer.Deserialize(reader, header, elementType, true, binarySize);
        }

        var property = new ArrayStructProperty
        {
            PropertyName = propertyName,
            PropertyType = propertyType,
            ElementType = elementType,
            UUID = (uuid1, uuid2, uuid3, uuid4),
            Values = values
        };

        return property;
    }

    private static BoolProperty DeserializeBoolProperty(BinaryReader reader)
    {
        _ = reader.ReadInt32();
        var index = reader.ReadInt32();
        var value = reader.ReadSByte();
        _ = reader.ReadSByte();

        var property = new BoolProperty
        {
            Index = index,
            Value = value
        };

        return property;
    }

    private ByteProperty DeserializeByteProperty(BinaryReader reader)
    {
        var binarySize = reader.ReadInt32();
        var index = reader.ReadInt32();
        var type = _stringSerializer.Deserialize(reader);
        _ = reader.ReadSByte();

        sbyte? byteData = null;
        string? stringData = null;

        if (string.Equals(type, "None", StringComparison.Ordinal))
            byteData = reader.ReadSByte();
        else
            stringData = _stringSerializer.Deserialize(reader);

        var property = new ByteProperty
        {
            Index = index,
            Type = type,
            ByteData = byteData,
            StringData = stringData
        };

        return property;
    }

    private EnumProperty DeserializeEnumProperty(BinaryReader reader)
    {
        var binarySize = reader.ReadInt32();
        var index = reader.ReadInt32();
        var type = _stringSerializer.Deserialize(reader);
        _ = reader.ReadSByte();
        var value = _stringSerializer.Deserialize(reader);

        var property = new EnumProperty
        {
            Index = index,
            Type = type,
            Value = value
        };

        return property;
    }

    private static FloatProperty DeserializeFloatProperty(BinaryReader reader)
    {
        var binarySize = reader.ReadInt32();
        var index = reader.ReadInt32();
        _ = reader.ReadSByte();
        var value = reader.ReadSingle();

        var property = new FloatProperty
        {
            Index = index,
            Value = value
        };

        return property;
    }

    private static DoubleProperty DeserializeDoubleProperty(BinaryReader reader)
    {
        var binarySize = reader.ReadInt32();
        var index = reader.ReadInt32();
        _ = reader.ReadSByte();
        var value = reader.ReadDouble();

        var property = new DoubleProperty
        {
            Index = index,
            Value = value
        };

        return property;
    }

    private static IntProperty DeserializeIntProperty(BinaryReader reader)
    {
        var binarySize = reader.ReadInt32();
        var index = reader.ReadInt32();
        _ = reader.ReadSByte();
        var value = reader.ReadInt32();

        var property = new IntProperty
        {
            Index = index,
            Value = value
        };

        return property;
    }

    private static Int8Property DeserializeInt8Property(BinaryReader reader)
    {
        var binarySize = reader.ReadInt32();
        var index = reader.ReadInt32();
        _ = reader.ReadSByte();
        var value = reader.ReadSByte();

        var property = new Int8Property
        {
            Index = index,
            Value = value
        };

        return property;
    }

    private static UInt32Property DeserializeUInt32Property(BinaryReader reader)
    {
        var binarySize = reader.ReadInt32();
        var index = reader.ReadInt32();
        _ = reader.ReadSByte();
        var value = reader.ReadUInt32();

        var property = new UInt32Property
        {
            Index = index,
            Value = value
        };

        return property;
    }

    private static Int64Property DeserializeInt64Property(BinaryReader reader)
    {
        var binarySize = reader.ReadInt32();
        var index = reader.ReadInt32();
        _ = reader.ReadSByte();
        var value = reader.ReadInt64();

        var property = new Int64Property
        {
            Index = index,
            Value = value
        };

        return property;
    }

    private static UInt64Property DeserializeUInt64Property(BinaryReader reader)
    {
        var binarySize = reader.ReadInt32();
        var index = reader.ReadInt32();
        _ = reader.ReadSByte();
        var value = reader.ReadUInt64();

        var property = new UInt64Property
        {
            Index = index,
            Value = value
        };

        return property;
    }

    private MapProperty DeserializeMapProperty(BinaryReader reader, Header header, string? propertyName = null, string? type = null)
    {
        var binarySize = reader.ReadInt32();
        var index = reader.ReadInt32();
        var keyType = _stringSerializer.Deserialize(reader);
        var valueType = _stringSerializer.Deserialize(reader);
        var padding = reader.ReadSByte();
        var modeType = reader.ReadInt32();

        if (modeType == 2)
        {
            var disc1 = _stringSerializer.Deserialize(reader);
            var disc2 = _stringSerializer.Deserialize(reader);
        }
        else if (modeType == 3)
        {
            var disc1 = _hexSerializer.Deserialize(reader, 9);
            var disc2 = _stringSerializer.Deserialize(reader);
            var disc3 = _stringSerializer.Deserialize(reader);
        }

        var count = reader.ReadInt32();

        var property = new MapProperty
        {
            Index = index,
            KeyType = keyType,
            ValueType = valueType,
            ModeType = modeType,
            Elements = new Dictionary<UnionBase, UnionBase?>(count)
        };

        UnionBase key;
        UnionBase? value;

        for (var i = 0; i < count; i++)
        {
            switch (property.KeyType)
            {
                case nameof(IntProperty):
                    key = new IntUnion { Value = reader.ReadInt32() };
                    break;
                case nameof(Int64Property):
                    key = new Int64Union { Value = reader.ReadInt64() };
                    break;
                case nameof(NameProperty):
                    key = new NameUnion { Value = _stringSerializer.Deserialize(reader) };
                    break;
                case nameof(StrProperty):
                    key = new StrUnion { Value = _stringSerializer.Deserialize(reader) };
                    break;
                case nameof(ObjectProperty):
                    key = new ObjectReferenceUnion { Value = _objectReferenceSerializer.Deserialize(reader) };
                    break;
                case nameof(EnumProperty):
                    key = new EnumUnion { Value = _stringSerializer.Deserialize(reader) };
                    break;
                case nameof(StructProperty):
                    if (string.Equals(propertyName, "Destroyed_Foliage_Transform", StringComparison.Ordinal))
                    {
                        key = header.SaveVersion >= 41
                            ? new Vector3DUnion { Value = _vectorSerializer.DeserializeVec3D(reader) }
                            : new Vector3Union { Value = _vectorSerializer.DeserializeVec3(reader) };
                        break;
                    }
                    if (string.Equals(type, "/BuildGunUtilities/BGU_Subsystem.BGU_Subsystem_C", StringComparison.Ordinal) || string.Equals(type, "/Script/NoImpure.NoImpureSubsystem")) //ToDo: Debug?
                    {
                        key = new Vector3Union { Value = _vectorSerializer.DeserializeVec3(reader) };
                        break;
                    }
                    if (string.Equals(propertyName, "mSaveData", StringComparison.Ordinal) || string.Equals(propertyName, "mUnresolvedSaveData", StringComparison.Ordinal))
                    {
                        key = new Vector3IUnion { Value = _vectorSerializer.DeserializeVec3I(reader) };
                        break;
                    }
                    key = new PropertiesUnion { Value = [.. DeserializeProperties(reader, header)] };
                    break;
                default:
                    throw new InvalidDataException("Unknown dictionary property key type");
            }
            switch (property.ValueType)
            {
                case nameof(ByteProperty):
                    value = new ByteUnion
                        {
                            Value = 
                                (property.KeyType == nameof(StrProperty) || string.Equals(property.KeyType, "/Script/NoImpure.NoImpureSubsystem", StringComparison.Ordinal))
                                ? Convert.ToSByte(_stringSerializer.Deserialize(reader))
                                : reader.ReadSByte()
                        };
                    break;
                case nameof(BoolProperty):
                    value = new BoolUnion { Value = reader.ReadSByte() };
                    break;
                case nameof(IntProperty):
                    value = new IntUnion { Value = reader.ReadInt32() };
                    break;
                case nameof(Int64Property):
                    value = new Int64Union { Value = reader.ReadInt64() };
                    break;
                case nameof(DoubleProperty):
                    value = new DoubleUnion { Value = reader.ReadDouble() };
                    break;
                case nameof(FloatProperty):
                    value = new FloatUnion { Value = reader.ReadSingle() };
                    break;
                case nameof(StrProperty):
                    value = string.Equals(type, "/BuildGunUtilities/BGU_Subsystem.BGU_Subsystem_C", StringComparison.Ordinal)
                        ? new StrUnion { Unknown1 = reader.ReadSingle(), Unknown2 = reader.ReadSingle(), Unknown3 = reader.ReadSingle(), Value = _stringSerializer.Deserialize(reader) }
                        : null;
                    break;
                case nameof(ObjectProperty):
                    value = string.Equals(type, "/BuildGunUtilities/BGU_Subsystem.BGU_Subsystem_C", StringComparison.Ordinal)
                        ? new ObjectReferenceUnion { Unknown1 = reader.ReadSingle(), Unknown2 = reader.ReadSingle(), Unknown3 = reader.ReadSingle(), Unknown4 = reader.ReadSingle(), Unknown5 = _stringSerializer.Deserialize(reader) }
                        : new ObjectReferenceUnion { Value = _objectReferenceSerializer.Deserialize(reader) };
                    break;
                case nameof(TextProperty):
                    value = new TextUnion { Value = DeserializeTextProperty(reader, header) };
                    break;
                case nameof(StructProperty):
                    if (string.Equals(type, "LBBalancerData", StringComparison.Ordinal))
                    {
                        value = new LBBalancerUnion { NormalIndex = reader.ReadInt32(), OverflowIndex = reader.ReadInt32(), FilterIndex = reader.ReadInt32() };
                        break;
                    }
                    if (string.Equals(type, "/StorageStatsRoom/Sub_SR.Sub_SR_C", StringComparison.Ordinal) || string.Equals(type, "/CentralStorage/Subsystem_SC.Subsystem_SC_C", StringComparison.Ordinal))
                    {
                        value = header.SaveVersion >= 41
                            ? new StorageDoubleUnion { Unknown1 = reader.ReadDouble(), Unknown2 = reader.ReadDouble(), Unknown3 = reader.ReadDouble() }
                            : new StorageSingleUnion { Unknown1 = reader.ReadSingle(), Unknown2 = reader.ReadSingle(), Unknown3 = reader.ReadSingle() };
                        break;
                    }
                    value = new PropertiesUnion { Value = [.. DeserializeProperties(reader, header)] };
                    break;
                default:
                    throw new InvalidDataException("Unknown dictionary property value type");
            }

            property.Elements.Add(key, value);
        }

        return property;
    }

    private NameProperty DeserializeNameProperty(BinaryReader reader)
    {
        var binarySize = reader.ReadInt32();
        var index = reader.ReadInt32();
        _ = reader.ReadSByte();
        var value = _stringSerializer.Deserialize(reader);

        var property = new NameProperty
        {
            Index = index,
            Value = value
        };

        return property;
    }

    private ObjectProperty DeserializeObjectProperty(BinaryReader reader)
    {
        var binarySize = reader.ReadInt32();
        var index = reader.ReadInt32();
        _ = reader.ReadSByte();
        var value = _objectReferenceSerializer.Deserialize(reader);

        var property = new ObjectProperty
        {
            Index = index,
            Value = value
        };

        return property;
    }

    private SoftObjectProperty DeserializeSoftObjectProperty(BinaryReader reader)
    {
        var binarySize = reader.ReadInt32();
        var index = reader.ReadInt32();
        _ = reader.ReadSByte();
        var value = _softObjectReferenceSerializer.Deserialize(reader);

        var property = new SoftObjectProperty
        {
            Index = index,
            Value = value,
        };

        return property;
    }

    private SetProperty DeserializeSetProperty(BinaryReader reader, string propertyName)
    {
        var binarySize = reader.ReadInt32();
        var index = reader.ReadInt32();
        var type = _stringSerializer.Deserialize(reader);
        _ = reader.ReadSByte();
        _ = reader.ReadInt32();
        var count = reader.ReadInt32();

        var property = new SetProperty
        {
            Index = index,
            Type = type,
            Elements = DeserializeUnions(reader, propertyName, count, type).ToArray()
        };

        return property;
    }

    private IEnumerable<UnionBase> DeserializeUnions(BinaryReader reader, string propertyName, int count, string type)
    {
        for (var i = 0; i < count; i++)
        {
            UnionBase value;
            switch (type)
            {
                case nameof(ObjectProperty):
                    value = new ObjectReferenceUnion { Value = _objectReferenceSerializer.Deserialize(reader) };
                    break;
                case nameof(StructProperty):
                    if (propertyName.Equals("/Script/FactoryGame.FGFoliageRemoval", StringComparison.Ordinal)
                        || propertyName.Equals("mRemovalLocations", StringComparison.Ordinal))
                    {
                        value = new Vector3Union { Value = _vectorSerializer.DeserializeVec3(reader) };
                        break;
                    }
                    if (propertyName.Equals("mDestroyedPickups", StringComparison.Ordinal)
                        || propertyName.Equals("mLootedDropPods", StringComparison.Ordinal)
                        || propertyName.Equals("/Script/FactoryGame.FGScannableSubsystem", StringComparison.Ordinal))
                    {
                        value = new GuidUnion { Value = new System.Guid(reader.ReadBytes(16)) };
                        break;
                    }
                    value = new FINNetworkUnion { Value = DeserializeFINNetworkProperty(reader) };
                    break;
                case nameof(NameProperty):
                    value = new NameUnion { Value = _stringSerializer.Deserialize(reader) };
                    break;
                case nameof(StrProperty):
                    value = new StrUnion { Value = _stringSerializer.Deserialize(reader) };
                    break;
                case nameof(IntProperty):
                    value = new IntUnion { Value = reader.ReadInt32() };
                    break;
                case nameof(UInt32Property):
                    value = new UInt32Union { Value = reader.ReadUInt32() };
                    break;
                default:
                    throw new InvalidDataException("Unknown set property value type");
            }

            yield return value;
        }
    }

    private FINNetworkProperty DeserializeFINNetworkProperty(BinaryReader reader)
    {
        var objectReference = _objectReferenceSerializer.Deserialize(reader);
        FINNetworkProperty? previous = null;
        string? step = null;
        if (reader.ReadInt32() == 1)
            previous = DeserializeFINNetworkProperty(reader);
        if (reader.ReadInt32() == 1)
            step = _stringSerializer.Deserialize(reader);

        return new FINNetworkProperty { ObjectReference = objectReference, Previous = previous, Step = step };
    }

    private StrProperty DeserializeStrProperty(BinaryReader reader)
    {
        var binarySize = reader.ReadInt32();
        var index = reader.ReadInt32();
        _ = reader.ReadSByte();
        var value = _stringSerializer.Deserialize(reader);

        var property = new StrProperty
        {
            Index = index,
            Value = value
        };

        return property;
    }

    private StructProperty DeserializeStructProperty(BinaryReader reader, Header header)
    {
        var binarySize = reader.ReadInt32();
        //var positionStart = reader.BaseStream.Position;
        var index = reader.ReadInt32();
        var type = _stringSerializer.Deserialize(reader);
        _ = reader.ReadInt64();
        _ = reader.ReadInt64();
        _ = reader.ReadSByte();
        var typedData = _typedDataSerializer.Deserialize(reader, header, type, false, binarySize);

        var property = new StructProperty
        {
            Index = index,
            Type = type,
            Value = typedData
        };

        //var expectedPosition = positionStart + binarySize;
        //if (expectedPosition != reader.BaseStream.Position)
        //{
        //    var hex = _hexSerializer.Deserialize(reader, (expectedPosition - reader.BaseStream.Position).ToInt());
        //    // throw new BadReadException("Expected stream position does not match actual position");
        //}

        return property;
    }
}
