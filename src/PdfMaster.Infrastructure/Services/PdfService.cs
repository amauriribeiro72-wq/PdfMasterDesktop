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
    public Task<string> MergeFilesAsync(IEnumerable<string> sourceFiles, string destinationPath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var filesList = sourceFiles.ToList();
            if (filesList.Count == 0)
                throw new ArgumentException("Nenhum arquivo informado para mesclar.");

            EnsureOutputDirectory(destinationPath);

            using var outputDocument = new PdfDocument();
            int totalFiles = filesList.Count;

            for (int i = 0; i < totalFiles; i++)
            {
                ct.ThrowIfCancellationRequested();
                var filePath = filesList[i];

                using var inputDocument = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
                int count = inputDocument.PageCount;
                for (int idx = 0; idx < count; idx++)
                {
                    ct.ThrowIfCancellationRequested();
                    var page = inputDocument.Pages[idx];
                    outputDocument.AddPage(page);
                }

                progress?.Report((double)(i + 1) / totalFiles * 100.0);
            }

            outputDocument.Save(destinationPath);
            return destinationPath;
        }, ct);
    }

    public Task<IEnumerable<string>> SplitAsync(string sourceFile, string outputFolder, IEnumerable<PageRange> ranges, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            Directory.CreateDirectory(outputFolder);
            var resultFiles = new List<string>();

            using var inputDocument = PdfReader.Open(sourceFile, PdfDocumentOpenMode.Import);
            int baseNameIndex = 1;

            foreach (var range in ranges)
            {
                ct.ThrowIfCancellationRequested();
                using var outputDoc = new PdfDocument();

                int start = Math.Max(1, range.Start);
                int end = Math.Min(inputDocument.PageCount, range.End);

                for (int i = start - 1; i < end; i++)
                {
                    outputDoc.AddPage(inputDocument.Pages[i]);
                }

                string outFileName = Path.Combine(outputFolder, $"{Path.GetFileNameWithoutExtension(sourceFile)}_parte_{baseNameIndex++}_pags_{start}-{end}.pdf");
                outputDoc.Save(outFileName);
                resultFiles.Add(outFileName);
            }

            return (IEnumerable<string>)resultFiles;
        }, ct);
    }

    public Task ReorderPagesAsync(string sourceFile, string destinationPath, IEnumerable<int> newPageIndices, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            EnsureOutputDirectory(destinationPath);
            using var inputDocument = PdfReader.Open(sourceFile, PdfDocumentOpenMode.Import);
            using var outputDocument = new PdfDocument();

            foreach (var index in newPageIndices)
            {
                ct.ThrowIfCancellationRequested();
                if (index >= 0 && index < inputDocument.PageCount)
                {
                    outputDocument.AddPage(inputDocument.Pages[index]);
                }
            }

            outputDocument.Save(destinationPath);
        }, ct);
    }

    public Task RotatePagesAsync(string sourceFile, string destinationPath, IDictionary<int, int> pageRotations, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            EnsureOutputDirectory(destinationPath);
            using var inputDocument = PdfReader.Open(sourceFile, PdfDocumentOpenMode.Import);
            using var outputDocument = new PdfDocument();

            for (int i = 0; i < inputDocument.PageCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                var page = outputDocument.AddPage(inputDocument.Pages[i]);

                if (pageRotations.TryGetValue(i, out int rotationAngle))
                {
                    int currentRotate = page.Rotate;
                    page.Rotate = (currentRotate + rotationAngle) % 360;
                }
            }

            outputDocument.Save(destinationPath);
        }, ct);
    }

    public Task DeletePagesAsync(string sourceFile, string destinationPath, IEnumerable<int> pagesToDelete, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            EnsureOutputDirectory(destinationPath);
            var toDeleteSet = new HashSet<int>(pagesToDelete);

            using var inputDocument = PdfReader.Open(sourceFile, PdfDocumentOpenMode.Import);
            using var outputDocument = new PdfDocument();

            for (int i = 0; i < inputDocument.PageCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (!toDeleteSet.Contains(i))
                {
                    outputDocument.AddPage(inputDocument.Pages[i]);
                }
            }

            outputDocument.Save(destinationPath);
        }, ct);
    }

    public Task ExtractPagesAsync(string sourceFile, string destinationPath, IEnumerable<int> pagesToExtract, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            EnsureOutputDirectory(destinationPath);
            var toExtractSet = new HashSet<int>(pagesToExtract);

            using var inputDocument = PdfReader.Open(sourceFile, PdfDocumentOpenMode.Import);
            using var outputDocument = new PdfDocument();

            for (int i = 0; i < inputDocument.PageCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (toExtractSet.Contains(i))
                {
                    outputDocument.AddPage(inputDocument.Pages[i]);
                }
            }

            outputDocument.Save(destinationPath);
        }, ct);
    }

    public Task AddWatermarkAsync(string sourceFile, string destinationPath, string watermarkText, double opacity = 0.3, double angleDegree = 45, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            EnsureOutputDirectory(destinationPath);
            using var doc = PdfReader.Open(sourceFile, PdfDocumentOpenMode.Modify);

            var font = new XFont("Arial", 42, XFontStyle.Bold);
            int alpha = (int)Math.Clamp(opacity * 255, 10, 255);
            var brush = new XSolidBrush(XColor.FromArgb(alpha, 128, 128, 128));

            foreach (var page in doc.Pages)
            {
                ct.ThrowIfCancellationRequested();
                using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

                var size = gfx.MeasureString(watermarkText, font);

                // Centraliza e rotaciona
                gfx.TranslateTransform(page.Width.Point / 2, page.Height.Point / 2);
                gfx.RotateTransform(-angleDegree);

                gfx.DrawString(
                    watermarkText,
                    font,
                    brush,
                    new XPoint(-size.Width / 2, size.Height / 4)
                );
            }

            doc.Save(destinationPath);
        }, ct);
    }

    public Task AddPageNumbersAsync(string sourceFile, string destinationPath, string formatMask = "Pág. {0} de {1}", bool isHeader = false, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            EnsureOutputDirectory(destinationPath);
            using var doc = PdfReader.Open(sourceFile, PdfDocumentOpenMode.Modify);
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

            doc.Save(destinationPath);
        }, ct);
    }

    public Task<long> CompressPdfAsync(string sourceFile, string destinationPath, CompressionProfile profile, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            EnsureOutputDirectory(destinationPath);

            using var input = PdfReader.Open(sourceFile, PdfDocumentOpenMode.Import);
            using var output = new PdfDocument();

            output.Options.CompressContentStreams = true;
            output.Options.NoCompression = false;
            output.Options.FlateEncodePageSegments = true;

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
                output.Info.Creator = "";
                output.Info.Producer = "PdfMaster Desktop";
            }

            output.Save(destinationPath);
            return new FileInfo(destinationPath).Length;
        }, ct);
    }

    public Task<DocumentMetadata> ReadMetadataAsync(string sourceFile, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var doc = PdfReader.Open(sourceFile, PdfDocumentOpenMode.InformationOnly);
            var info = doc.Info;

            return new DocumentMetadata(
                Title: info.Title,
                Author: info.Author,
                Subject: info.Subject,
                Keywords: info.Keywords,
                Creator: info.Creator,
                Producer: info.Producer,
                CreationDate: info.CreationDate,
                ModificationDate: info.ModificationDate,
                PageCount: doc.PageCount
            );
        }, ct);
    }

    public Task SanitizeMetadataAsync(string sourceFile, string destinationPath, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            EnsureOutputDirectory(destinationPath);
            using var doc = PdfReader.Open(sourceFile, PdfDocumentOpenMode.Modify);

            doc.Info.Title = "";
            doc.Info.Author = "";
            doc.Info.Subject = "";
            doc.Info.Keywords = "";
            doc.Info.Creator = "";
            doc.Info.Producer = "";

            doc.Save(destinationPath);
        }, ct);
    }

    public Task ProtectPdfAsync(string sourceFile, string destinationPath, string userPassword, string ownerPassword, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            EnsureOutputDirectory(destinationPath);
            using var doc = PdfReader.Open(sourceFile, PdfDocumentOpenMode.Modify);

            doc.SecuritySettings.UserPassword = userPassword;
            doc.SecuritySettings.OwnerPassword = string.IsNullOrEmpty(ownerPassword) ? userPassword : ownerPassword;
            doc.SecuritySettings.PermitPrint = true;
            doc.SecuritySettings.PermitModifyDocument = false;
            doc.SecuritySettings.PermitExtractContent = false;

            doc.Save(destinationPath);
        }, ct);
    }

    public Task UnlockPdfAsync(string sourceFile, string destinationPath, string password, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            EnsureOutputDirectory(destinationPath);
            using var doc = PdfReader.Open(sourceFile, password, PdfDocumentOpenMode.Modify);

            doc.SecuritySettings.UserPassword = "";
            doc.SecuritySettings.OwnerPassword = "";

            doc.Save(destinationPath);
        }, ct);
    }

    private static void EnsureOutputDirectory(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }
}
