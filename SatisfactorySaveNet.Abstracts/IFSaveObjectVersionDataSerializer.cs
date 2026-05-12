using SatisfactorySaveNet.Abstracts.Model;
using System.IO;

namespace SatisfactorySaveNet.Abstracts;

public interface IFSaveObjectVersionDataSerializer
{
    public FSaveObjectVersionData Deserialize(BinaryReader reader);
}
