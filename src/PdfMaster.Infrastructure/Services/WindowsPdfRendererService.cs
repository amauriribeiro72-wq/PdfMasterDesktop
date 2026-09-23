using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;
using PdfMaster.Core.Interfaces;
using PdfMaster.Core.Models;

namespace PdfMaster.Infrastructure.Services;

public class WindowsPdfRendererService : IPdfRendererService
{
    public async Task<IReadOnlyList<PageThumbnail>> RenderThumbnailsAsync(string filePath, int targetWidth = 240, CancellationToken ct = default)
    {
        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
            return Array.Empty<PageThumbnail>();

        var thumbnails = new List<PageThumbnail>();

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(fullPath);
            var pdfDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
            int count = (int)pdfDoc.PageCount;

            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                using var page = pdfDoc.GetPage((uint)i);
                using var memStream = new InMemoryRandomAccessStream();

                var options = new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)Math.Max(100, targetWidth)
                };

                await page.RenderToStreamAsync(memStream, options);

                using var netStream = memStream.AsStreamForRead();
                using var ms = new MemoryStream();
                await netStream.CopyToAsync(ms, ct);

                thumbnails.Add(new PageThumbnail(
                    PageIndex: i,
                    PageNumber: i + 1,
                    ImageBytes: ms.ToArray(),
                    Width: page.Size.Width,
                    Height: page.Size.Height
                ));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Retorna miniaturas vazias em caso de erro de leitura ou arquivo bloqueado
        }

        return thumbnails;
    }

    public async Task<byte[]> RenderPageAsync(string filePath, int pageIndex, int targetWidth = 1200, CancellationToken ct = default)
    {
        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
            return Array.Empty<byte>();

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(fullPath);
            var pdfDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);

            if (pageIndex < 0 || pageIndex >= (int)pdfDoc.PageCount)
                return Array.Empty<byte>();

            using var page = pdfDoc.GetPage((uint)pageIndex);
            using var memStream = new InMemoryRandomAccessStream();

            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Max(300, targetWidth)
            };

            await page.RenderToStreamAsync(memStream, options);

            using var netStream = memStream.AsStreamForRead();
            using var ms = new MemoryStream();
            await netStream.CopyToAsync(ms, ct);

            return ms.ToArray();
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }
}
