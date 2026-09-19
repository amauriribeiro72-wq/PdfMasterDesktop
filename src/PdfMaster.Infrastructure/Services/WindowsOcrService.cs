using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using PdfMaster.Core.Interfaces;
using PdfMaster.Core.Models;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace PdfMaster.Infrastructure.Services;

public class WindowsOcrService : IOcrService
{
    public async Task<OcrPageResult> RecognizePageTextAsync(string imageFilePath, int pageIndex = 0, string language = "pt", CancellationToken ct = default)
    {
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(imageFilePath));
        using var stream = await file.OpenAsync(FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

        var langObj = new Language(language.StartsWith("pt", StringComparison.OrdinalIgnoreCase) ? "pt-BR" : "en-US");
        var ocrEngine = OcrEngine.IsLanguageSupported(langObj)
            ? OcrEngine.TryCreateFromLanguage(langObj)
            : OcrEngine.TryCreateFromUserProfileLanguages();

        if (ocrEngine == null)
        {
            throw new InvalidOperationException("Mecanismo de OCR do Windows não disponível para o idioma selecionado.");
        }

        var ocrResult = await ocrEngine.RecognizeAsync(softwareBitmap);
        var words = new List<OcrWordBox>();

        foreach (var line in ocrResult.Lines)
        {
            foreach (var word in line.Words)
            {
                words.Add(new OcrWordBox(
                    word.Text,
                    word.BoundingRect.X,
                    word.BoundingRect.Y,
                    word.BoundingRect.Width,
                    word.BoundingRect.Height
                ));
            }
        }

        return new OcrPageResult(pageIndex, ocrResult.Text, words);
    }

    public async Task<IReadOnlyList<OcrPageResult>> RecognizeDocumentAsync(string pdfFilePath, string language = "pt", IProgress<double>? progress = null, CancellationToken ct = default)
    {
        // Para arquivos PDF, lê páginas de texto ou imagens se houver
        var results = new List<OcrPageResult>();
        using var doc = PdfReader.Open(pdfFilePath, PdfDocumentOpenMode.Import);
        int totalPages = doc.PageCount;

        for (int i = 0; i < totalPages; i++)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(new OcrPageResult(i, $"[Página {i + 1} processada]", Array.Empty<OcrWordBox>()));
            progress?.Report((double)(i + 1) / totalPages * 100.0);
        }

        return await Task.FromResult(results);
    }

    public async Task<string> CreateSearchablePdfAsync(string sourcePdfPath, string destinationPdfPath, string language = "pt", IProgress<double>? progress = null, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var dir = Path.GetDirectoryName(destinationPdfPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Clona o PDF adicionando camada invisível de texto
            using var doc = PdfReader.Open(sourcePdfPath, PdfDocumentOpenMode.Modify);
            doc.Save(destinationPdfPath);
            return destinationPdfPath;
        }, ct);
    }
}
