using SatisfactorySaveNet.Abstracts.Model;
using SatisfactorySaveNet.Abstracts.Model.Properties;
using System.Collections.Generic;
using System.IO;

namespace SatisfactorySaveNet.Abstracts;

public interface IPropertySerializer
{
    /// <summary>
    /// Deserializes the property list for one object. <paramref name="saveVersion"/> is
    /// the per-object SaveCustomVersion captured from the data blob — used to gate the
    /// v1.2+ FPropertyTag-complete-type-name format and the leading serializationControl byte.
    /// </summary>
    public IEnumerable<Property> DeserializeProperties(BinaryReader reader, Header? header = null, string? type = null, long? expectedPosition = null, int? saveVersion = null);

    public Property? DeserializeProperty(BinaryReader reader, Header? header = null, string? type = null, int? saveVersion = null);
}
