using PdfMaster.Core.Models;

namespace PdfMaster.Core.Interfaces;

public interface IOcrService
{
    Task<OcrPageResult> RecognizePageTextAsync(string imageFilePath, int pageIndex = 0, string language = "pt", CancellationToken ct = default);
    Task<IReadOnlyList<OcrPageResult>> RecognizeDocumentAsync(string pdfFilePath, string language = "pt", IProgress<double>? progress = null, CancellationToken ct = default);
    Task<string> CreateSearchablePdfAsync(string sourcePdfPath, string destinationPdfPath, string language = "pt", IProgress<double>? progress = null, CancellationToken ct = default);
}
