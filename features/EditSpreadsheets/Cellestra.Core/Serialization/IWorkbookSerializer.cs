using Cellestra.Core.Model;

namespace Cellestra.Core.Serialization;

/// <summary>Reads and writes a <see cref="Workbook"/> to a stream in a particular format.</summary>
public interface IWorkbookSerializer
{
    Workbook Read(Stream stream);

    void Write(Workbook workbook, Stream stream);
}
