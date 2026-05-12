using SatisfactorySaveNet.Abstracts.Model;
using System.IO;

namespace SatisfactorySaveNet.Abstracts;

public interface IObjectSerializer
{
    /// <summary>
    /// Deserializes a single object body. <paramref name="saveVersion"/> is the level's
    /// SaveCustomVersion (or the header's SaveVersion for the persistent level) — used to
    /// gate post-body fields introduced at SaveCustomVersion 53+.
    /// </summary>
    public ComponentObject Deserialize(BinaryReader reader, Header header, ComponentObject componentObject, int? saveVersion = null);
}
