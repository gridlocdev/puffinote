using Cellestra.Core.Model;

namespace Cellestra.Core.Serialization.Ods;

/// <summary>The <c>.ods</c> implementation of <see cref="IWorkbookSerializer"/>.</summary>
public sealed class OdsSerializer : IWorkbookSerializer
{
    public Workbook Read(Stream stream) => OdsReader.Read(stream);

    public void Write(Workbook workbook, Stream stream) => OdsWriter.Write(workbook, stream);
}
