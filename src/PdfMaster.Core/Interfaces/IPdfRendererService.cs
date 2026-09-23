using PdfMaster.Core.Models;

namespace PdfMaster.Core.Interfaces;

public interface IPdfRendererService
{
    Task<IReadOnlyList<PageThumbnail>> RenderThumbnailsAsync(string filePath, int targetWidth = 240, CancellationToken ct = default);
    Task<byte[]> RenderPageAsync(string filePath, int pageIndex, int targetWidth = 1200, CancellationToken ct = default);
}
