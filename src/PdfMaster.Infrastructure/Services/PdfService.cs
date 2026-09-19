using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PdfMaster.Core.Interfaces;
using PdfMaster.Core.Models;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace PdfMaster.Infrastructure.Services;

public class PdfService : IPdfService
{
    private static MemoryStream LoadToMemory(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var ms = new MemoryStream();
        fs.CopyTo(ms);
        ms.Position = 0;
        return ms;
    }

    private static void SafeSaveDocument(PdfDocument document, string destinationPath)
    {
        var fullDest = Path.GetFullPath(destinationPath);
        var dir = Path.GetDirectoryName(fullDest);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string tempFile = Path.Combine(dir ?? ".", $"{Path.GetFileName(fullDest)}.tmp_{Guid.NewGuid():N}");
        try
        {
            document.Save(tempFile);
            File.Move(tempFile, fullDest, overwrite: true);
        }
        catch (IOException ioEx)
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            throw new IOException($"O arquivo de destino '{Path.GetFileName(fullDest)}' está aberto em outro aplicativo (ex: Adobe Reader ou navegador). Feche-o e tente novamente.", ioEx);
        }
        catch
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            throw;
        }
    }

    public Task<string> MergeFilesAsync(IEnumerable<string> sourceFiles, string destinationPath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var filesList = sourceFiles.Select(Path.GetFullPath).ToList();
            if (filesList.Count == 0)
                throw new ArgumentException("Nenhum arquivo informado para mesclar.");

            using var outputDocument = new PdfDocument();
            int totalFiles = filesList.Count;

            for (int i = 0; i < totalFiles; i++)
            {
                ct.ThrowIfCancellationRequested();
                var filePath = filesList[i];

                try
                {
                    using var ms = LoadToMemory(filePath);
                    using var inputDocument = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
                    for (int idx = 0; idx < inputDocument.PageCount; idx++)
                    {
                        ct.ThrowIfCancellationRequested();
                        outputDocument.AddPage(inputDocument.Pages[idx]);
                    }
                }
                catch (PdfReaderException ex)
                {
                    throw new InvalidOperationException($"O arquivo '{Path.GetFileName(filePath)}' está protegido por senha ou corrompido.", ex);
                }

                progress?.Report((double)(i + 1) / totalFiles * 100.0);
            }

            if (outputDocument.PageCount == 0)
                throw new InvalidOperationException("Nenhuma página pôde ser extraída dos documentos fornecidos.");

            SafeSaveDocument(outputDocument, destinationPath);
            return destinationPath;
        }, ct);
    }

    public Task<IEnumerable<string>> SplitAsync(string sourceFile, string outputFolder, IEnumerable<PageRange> ranges, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var fullSource = Path.GetFullPath(sourceFile);
            var fullOutputFolder = Path.GetFullPath(outputFolder);
            Directory.CreateDirectory(fullOutputFolder);

            var resultFiles = new List<string>();
            using var ms = LoadToMemory(fullSource);
            using var inputDocument = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
            int baseNameIndex = 1;

            foreach (var range in ranges)
            {
                ct.ThrowIfCancellationRequested();
                int start = Math.Max(1, range.Start);
                int end = Math.Min(inputDocument.PageCount, range.End);

                if (start > end || start > inputDocument.PageCount) continue;

                using var outputDoc = new PdfDocument();
                for (int i = start - 1; i < end; i++)
                {
                    outputDoc.AddPage(inputDocument.Pages[i]);
                }

                if (outputDoc.PageCount > 0)
                {
                    string outFileName = Path.Combine(fullOutputFolder, $"{Path.GetFileNameWithoutExtension(fullSource)}_parte_{baseNameIndex++}_pags_{start}-{end}.pdf");
                    SafeSaveDocument(outputDoc, outFileName);
                    resultFiles.Add(outFileName);
                }
            }

            return (IEnumerable<string>)resultFiles;
        }, ct);
    }

    public Task ReorderPagesAsync(string sourceFile, string destinationPath, IEnumerable<int> newPageIndices, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var ms = LoadToMemory(sourceFile);
            using var inputDocument = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
            using var outputDocument = new PdfDocument();

            foreach (var index in newPageIndices)
            {
                ct.ThrowIfCancellationRequested();
                if (index >= 0 && index < inputDocument.PageCount)
                {
                    outputDocument.AddPage(inputDocument.Pages[index]);
                }
            }

            if (outputDocument.PageCount == 0)
                throw new InvalidOperationException("A lista de índices resultou em um documento sem páginas.");

            SafeSaveDocument(outputDocument, destinationPath);
        }, ct);
    }

    public Task RotatePagesAsync(string sourceFile, string destinationPath, IDictionary<int, int> pageRotations, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var ms = LoadToMemory(sourceFile);
            using var inputDocument = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
            using var outputDocument = new PdfDocument();

            for (int i = 0; i < inputDocument.PageCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                var page = outputDocument.AddPage(inputDocument.Pages[i]);

                if (pageRotations.TryGetValue(i, out int rotationAngle))
                {
                    int currentRotate = page.Rotate;
                    page.Rotate = ((currentRotate + rotationAngle) % 360 + 360) % 360;
                }
            }

            SafeSaveDocument(outputDocument, destinationPath);
        }, ct);
    }

    public Task DeletePagesAsync(string sourceFile, string destinationPath, IEnumerable<int> pagesToDelete, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var toDeleteSet = new HashSet<int>(pagesToDelete);
            using var ms = LoadToMemory(sourceFile);
            using var inputDocument = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
            using var outputDocument = new PdfDocument();

            for (int i = 0; i < inputDocument.PageCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (!toDeleteSet.Contains(i))
                {
                    outputDocument.AddPage(inputDocument.Pages[i]);
                }
            }

            if (outputDocument.PageCount == 0)
                throw new InvalidOperationException("Não é possível salvar um documento PDF com 0 páginas.");

            SafeSaveDocument(outputDocument, destinationPath);
        }, ct);
    }

    public Task ExtractPagesAsync(string sourceFile, string destinationPath, IEnumerable<int> pagesToExtract, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var toExtractSet = new HashSet<int>(pagesToExtract);
            using var ms = LoadToMemory(sourceFile);
            using var inputDocument = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
            using var outputDocument = new PdfDocument();

            for (int i = 0; i < inputDocument.PageCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (toExtractSet.Contains(i))
                {
                    outputDocument.AddPage(inputDocument.Pages[i]);
                }
            }

            if (outputDocument.PageCount == 0)
                throw new InvalidOperationException("Nenhuma página selecionada para extração.");

            SafeSaveDocument(outputDocument, destinationPath);
        }, ct);
    }

    public Task AddWatermarkAsync(string sourceFile, string destinationPath, string watermarkText, double opacity = 0.3, double angleDegree = 45, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var ms = LoadToMemory(sourceFile);
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);

            var font = new XFont("Arial", 42, XFontStyle.Bold);
            int alpha = (int)Math.Clamp(opacity * 255, 10, 255);
            var brush = new XSolidBrush(XColor.FromArgb(alpha, 128, 128, 128));

            foreach (var page in doc.Pages)
            {
                ct.ThrowIfCancellationRequested();
                using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                var size = gfx.MeasureString(watermarkText, font);

                gfx.TranslateTransform(page.Width.Point / 2, page.Height.Point / 2);
                gfx.RotateTransform(-angleDegree);
                gfx.DrawString(watermarkText, font, brush, new XPoint(-size.Width / 2, size.Height / 4));
            }

            SafeSaveDocument(doc, destinationPath);
        }, ct);
    }

    public Task AddPageNumbersAsync(string sourceFile, string destinationPath, string formatMask = "Pág. {0} de {1}", bool isHeader = false, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var ms = LoadToMemory(sourceFile);
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);
            var font = new XFont("Arial", 10, XFontStyle.Regular);
            var brush = new XSolidBrush(XColor.FromArgb(200, 60, 60, 60));
            int totalPages = doc.PageCount;

            for (int i = 0; i < totalPages; i++)
            {
                ct.ThrowIfCancellationRequested();
                var page = doc.Pages[i];
                using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

                string text = string.Format(formatMask, i + 1, totalPages);
                var size = gfx.MeasureString(text, font);

                double x = (page.Width.Point - size.Width) / 2;
                double y = isHeader ? 30 : page.Height.Point - 25;

                gfx.DrawString(text, font, brush, new XPoint(x, y));
            }

            SafeSaveDocument(doc, destinationPath);
        }, ct);
    }

    public Task<long> CompressPdfAsync(string sourceFile, string destinationPath, CompressionProfile profile, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var ms = LoadToMemory(sourceFile);
            using var input = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
            using var output = new PdfDocument();

            output.Options.CompressContentStreams = true;
            output.Options.NoCompression = false;

            int total = input.PageCount;
            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                output.AddPage(input.Pages[i]);
                progress?.Report((double)(i + 1) / total * 100.0);
            }

            if (profile.RemoveMetadata)
            {
                output.Info.Title = "";
                output.Info.Author = "";
                output.Info.Subject = "";
                output.Info.Keywords = "";
                try { output.Info.Creator = ""; } catch { }
            }

            SafeSaveDocument(output, destinationPath);
            return new FileInfo(destinationPath).Length;
        }, ct);
    }

    public Task<DocumentMetadata> ReadMetadataAsync(string sourceFile, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var ms = LoadToMemory(sourceFile);
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.InformationOnly);
            var info = doc.Info;

            DateTime? cDate = null;
            DateTime? mDate = null;
            try { cDate = info.CreationDate; } catch { }
            try { mDate = info.ModificationDate; } catch { }

            return new DocumentMetadata(
                Title: info.Title,
                Author: info.Author,
                Subject: info.Subject,
                Keywords: info.Keywords,
                Creator: info.Creator,
                Producer: info.Producer,
                CreationDate: cDate,
                ModificationDate: mDate,
                PageCount: doc.PageCount
            );
        }, ct);
    }

    public Task SanitizeMetadataAsync(string sourceFile, string destinationPath, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var ms = LoadToMemory(sourceFile);
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);

            doc.Info.Title = "";
            doc.Info.Author = "";
            doc.Info.Subject = "";
            doc.Info.Keywords = "";
            try { doc.Info.Creator = ""; } catch { }
            try { doc.Info.Elements.Remove("/Producer"); } catch { }

            SafeSaveDocument(doc, destinationPath);
        }, ct);
    }

    public Task ProtectPdfAsync(string sourceFile, string destinationPath, string userPassword, string ownerPassword, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var ms = LoadToMemory(sourceFile);
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);

            doc.SecuritySettings.UserPassword = userPassword;
            doc.SecuritySettings.OwnerPassword = string.IsNullOrEmpty(ownerPassword) ? userPassword : ownerPassword;
            doc.SecuritySettings.PermitPrint = true;
            doc.SecuritySettings.PermitModifyDocument = false;
            doc.SecuritySettings.PermitExtractContent = false;

            SafeSaveDocument(doc, destinationPath);
        }, ct);
    }

    public Task UnlockPdfAsync(string sourceFile, string destinationPath, string password, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var ms = LoadToMemory(sourceFile);
            PdfDocument doc;
            try
            {
                doc = PdfReader.Open(ms, password, PdfDocumentOpenMode.Modify);
            }
            catch (PdfReaderException ex)
            {
                throw new InvalidOperationException("A senha fornecida para o documento está incorreta ou o arquivo está danificado.", ex);
            }

            using (doc)
            {
                doc.SecuritySettings.UserPassword = "";
                doc.SecuritySettings.OwnerPassword = "";
                SafeSaveDocument(doc, destinationPath);
            }
        }, ct);
    }
}
