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

    /// <summary>Parsed value when <see cref="Type"/> == "ObjectProperty".</summary>
    public ObjectReferenceValue? ObjectValue { get; set; }

    /// <summary>Parsed values when <see cref="Type"/> == "ArrayProperty" and the
    /// element type is "ObjectProperty".</summary>
    public IReadOnlyList<ObjectReferenceValue>? ArrayObjectValues { get; set; }

    /// <summary>Parsed string when <see cref="Type"/> == "StrProperty" or "NameProperty".</summary>
    public string? StringValue { get; set; }
}

/// <summary>An Unreal <c>FObjectReference</c> — a (levelName, pathName) pair.</summary>
public readonly record struct ObjectReferenceValue(string LevelName, string PathName);

