using System;
using System.Collections.Generic;

namespace SatisfactorySaveNet.Abstracts.Model.Properties;

/// <summary>
/// A v1.2+ property whose tag (name, type tree, binary size, flags, optional GUID) was
/// parsed. For a handful of common property types (ObjectProperty, ArrayProperty whose
/// element is ObjectProperty, StrProperty / NameProperty) the value is also parsed so
/// callers can resolve recipes, mined resources, fuels, etc. without re-running the
/// parse. Other property types leave the value bytes unread (but stream-aligned).
/// </summary>
public class RawProperty : Property
{
    public override PropertyConstraint PropertyValueType => PropertyConstraint.Raw;

    public required string Type { get; set; }
    public FPropertyTagNode? TypeNode { get; set; }
    public int BinarySize { get; set; }
    public byte Flags { get; set; }
    public Guid? PropertyGuid { get; set; }

    // ---- scalars ----

    public int?    IntValue    { get; set; }   // IntProperty
    public uint?   UIntValue   { get; set; }   // UInt32Property
    public long?   LongValue   { get; set; }   // Int64Property
    public ulong?  ULongValue  { get; set; }   // UInt64Property
    public sbyte?  SByteValue  { get; set; }   // Int8Property
    public float?  FloatValue  { get; set; }   // FloatProperty
    public double? DoubleValue { get; set; }   // DoubleProperty
    /// <summary>BoolProperty value — at v1.2 it lives in tag flag bit 0x10, not in the value bytes.</summary>
    public bool?   BoolValue   { get; set; }

    /// <summary>Parsed value when <see cref="Type"/> == "ObjectProperty".</summary>
    public ObjectReferenceValue? ObjectValue { get; set; }

    /// <summary>Parsed values when <see cref="Type"/> == "ArrayProperty" and the
    /// element type is "ObjectProperty".</summary>
    public IReadOnlyList<ObjectReferenceValue>? ArrayObjectValues { get; set; }

    /// <summary>Parsed string when <see cref="Type"/> == "StrProperty" or "NameProperty".</summary>
    public string? StringValue { get; set; }

    /// <summary>StructProperty value for the fixed-size raw struct types we recognise
    /// (Vector/Rotator/Quat/Box/Color/LinearColor/Guid/IntPoint/DateTime/TimerHandle/
    /// SlateBrush/FluidBox/RailroadTrackPosition). Unknown / property-list-shaped
    /// structs leave this null and the value bytes are skipped via the binary-size fence.</summary>
    public StructValue? StructValue { get; set; }

    /// <summary>MapProperty entries when every key and value type is a fixed-size primitive
    /// or framed string. Maps with composite key/value types leave this null and the
    /// value bytes are skipped via the binary-size fence.</summary>
    public IReadOnlyList<MapEntryValue>? MapEntries { get; set; }

    /// <summary>Parsed elements when <see cref="Type"/> == "ArrayProperty" and the
    /// element type is "StructProperty" (e.g. <c>Array&lt;Struct&lt;SplinePointData&gt;&gt;</c>
    /// on pipes and similar route-carrying actors). Each entry exposes the inner
    /// property list that makes up one struct element. The struct subtype name lives
    /// in <see cref="FPropertyTagNode.Children"/> of <see cref="TypeNode"/>.</summary>
    public IReadOnlyList<StructElementValue>? ArrayStructValues { get; set; }
}

/// <summary>One element of an <c>ArrayProperty&lt;StructProperty&gt;</c> — a list of
/// inner properties (each itself a <see cref="RawProperty"/> at v1.2+).</summary>
public sealed record StructElementValue(IReadOnlyList<Property> Properties);

/// <summary>
/// StructProperty value. <see cref="Value"/> is one of:
///   <see cref="double"/>[] (Vector/Rotator/Vector2D/Quat/Vector4)
///   <see cref="float"/>[]  (LinearColor)
///   <see cref="BoxValue"/>
///   <see cref="byte"/>[]   (Color = 4 bytes BGRA, Guid = 16 bytes)
///   <see cref="long"/>     (IntPoint, DateTime)
///   <see cref="float"/>    (FluidBox)
///   <see cref="string"/>   (TimerHandle, SlateBrush)
///   <see cref="RailroadTrackPositionValue"/>
///   null                   (unrecognised type; bytes were skipped via the fence).
/// </summary>
public sealed record StructValue(string TypeName, object? Value);

public readonly record struct BoxValue(double[] Min, double[] Max, bool IsValid);
public readonly record struct RailroadTrackPositionValue(string Root, string InstanceName, float Offset, float Forward);

/// <summary>MapProperty entry. Key/Value runtime types depend on the map's declared
/// key/value types — int / long / string / ObjectReferenceValue / byte / bool / float / double.</summary>
public readonly record struct MapEntryValue(object Key, object Value);

/// <summary>An Unreal <c>FObjectReference</c> — a (levelName, pathName) pair.</summary>
public readonly record struct ObjectReferenceValue(string LevelName, string PathName);

