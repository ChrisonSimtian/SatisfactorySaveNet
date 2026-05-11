using System;

namespace SatisfactorySaveNet.Abstracts.Model.Properties;

/// <summary>
/// A v1.2+ property whose tag (name, type tree, binary size, flags, optional GUID) was
/// parsed but whose value bytes are not deeply deserialized. Use the type tree to dispatch
/// to per-type readers later; for now this keeps the stream correctly aligned.
/// </summary>
public class RawProperty : Property
{
    public override PropertyConstraint PropertyValueType => PropertyConstraint.Raw;

    public required string Type { get; set; }
    public FPropertyTagNode? TypeNode { get; set; }
    public int BinarySize { get; set; }
    public byte Flags { get; set; }
    public Guid? PropertyGuid { get; set; }
}
