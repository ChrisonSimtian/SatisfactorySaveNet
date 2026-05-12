using System.Collections.Generic;

namespace SatisfactorySaveNet.Abstracts.Model;

/// <summary>
/// Recursive type-tree node used by the v1.2+ FPropertyTag.IsCompletePropertyTagType
/// format. Encodes the property's type name plus any nested type info (e.g.
/// <c>ArrayProperty(StructProperty(InventoryStack))</c>) that previously lived in
/// per-type tag-extension fields.
/// </summary>
public class FPropertyTagNode
{
    public string Name { get; set; } = string.Empty;
    public ICollection<FPropertyTagNode> Children { get; set; } = [];
}
