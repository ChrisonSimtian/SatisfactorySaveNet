namespace SatisfactorySaveNet.Abstracts.Model.Properties;

public enum PropertyConstraint
{
    Array,
    Bool,
    SByte,
    Float,
    Enum,
    FINNetwork,
    Double,
    Int64,
    Int8,
    Interface,
    Int32,
    Map,
    Name,
    Object,
    Set,
    SoftObject,
    String,
    Struct,
    Text,
    UInt32,
    UInt64,

    /// <summary>
    /// New v1.2+ property whose tag was parsed via FPropertyTag's complete-type-name
    /// format but whose value bytes are not yet deeply deserialized. Stream-aligned
    /// (binary size is consumed) but the value is opaque.
    /// </summary>
    Raw
}
