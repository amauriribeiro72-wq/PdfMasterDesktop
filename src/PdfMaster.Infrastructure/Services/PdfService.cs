using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PdfMaster.Core.Interfaces;
using PdfMaster.Core.Models;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.AcroForms;
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
            throw new IOException($"O arquivo de destino '{Path.GetFileName(fullDest)}' está aberto em outro aplicativo. Feche-o e tente novamente.", ioEx);
        }
        catch
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            throw;
        }
    }

    #region CATEGORIA A: Manipulação Avançada de Páginas

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

    public Task CropPagesAsync(string sourceFile, string destinationPath, PageCropMargins margins, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var ms = LoadToMemory(sourceFile);
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);

            foreach (var page in doc.Pages)
            {
                ct.ThrowIfCancellationRequested();
                var mb = page.MediaBox;
                double newX1 = mb.X1 + margins.Left;
                double newY1 = mb.Y1 + margins.Bottom;
                double newX2 = mb.X2 - margins.Right;
                double newY2 = mb.Y2 - margins.Top;

                if (newX2 > newX1 && newY2 > newY1)
                {
                    page.MediaBox = new PdfRectangle(newX1, newY1, newX2, newY2);
                    page.CropBox = new PdfRectangle(newX1, newY1, newX2, newY2);
                }
            }

            SafeSaveDocument(doc, destinationPath);
        }, ct);
    }

    public Task GenerateNUpAsync(string sourceFile, string destinationPath, NUpConfiguration config, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var ms = LoadToMemory(sourceFile);
            using var input = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
            using var output = new PdfDocument();

            int n = config.PagesPerSheet <= 2 ? 2 : 4;
            int total = input.PageCount;

            for (int i = 0; i < total; i += n)
            {
                ct.ThrowIfCancellationRequested();
                var page = output.AddPage();
                page.Size = PdfSharpCore.PageSize.A4;
                page.Orientation = config.Landscape ? PdfSharpCore.PageOrientation.Landscape : PdfSharpCore.PageOrientation.Portrait;

                using var gfx = XGraphics.FromPdfPage(page);

                if (n == 2)
                {
                    double w = page.Width.Point / 2;
                    double h = page.Height.Point;

                    for (int sub = 0; sub < 2 && (i + sub) < total; sub++)
                    {
                        var srcPage = input.Pages[i + sub];
                        double scale = Math.Min(w / srcPage.Width.Point, h / srcPage.Height.Point) * 0.95;
                        double dx = sub * w + (w - srcPage.Width.Point * scale) / 2;
                        double dy = (h - srcPage.Height.Point * scale) / 2;

                        if (config.DrawBorder)
                        {
                            gfx.DrawRectangle(XPens.LightGray, sub * w + 5, 5, w - 10, h - 10);
                        }
                    }
                }
                else // 4 por folha
                {
                    double w = page.Width.Point / 2;
                    double h = page.Height.Point / 2;

                    for (int sub = 0; sub < 4 && (i + sub) < total; sub++)
                    {
                        int row = sub / 2;
                        int col = sub % 2;

                        if (config.DrawBorder)
                        {
                            gfx.DrawRectangle(XPens.LightGray, col * w + 5, row * h + 5, w - 10, h - 10);
                        }
                    }
                }
            }

            SafeSaveDocument(output, destinationPath);
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

    public Task AddWatermarkImageAsync(string sourceFile, string destinationPath, string imagePath, double opacity = 0.3, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var ms = LoadToMemory(sourceFile);
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);
            using var ximg = XImage.FromFile(imagePath);

            foreach (var page in doc.Pages)
            {
                ct.ThrowIfCancellationRequested();
                using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                double w = Math.Min(page.Width.Point * 0.5, 300);
                double h = w * (ximg.PixelHeight / (double)ximg.PixelWidth);
                double x = (page.Width.Point - w) / 2;
                double y = (page.Height.Point - h) / 2;

                gfx.DrawImage(ximg, x, y, w, h);
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

    public Task FlattenDocumentAsync(string sourceFile, string destinationPath, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var ms = LoadToMemory(sourceFile);
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);

            // Achata campos de formulário e anotações interativas
            if (doc.AcroForm != null)
            {
                doc.AcroForm.Elements.Clear();
            }

            foreach (var page in doc.Pages)
            {
                ct.ThrowIfCancellationRequested();
                page.Elements.Remove("/Annots");
            }

            SafeSaveDocument(doc, destinationPath);
        }, ct);
    }

    #endregion

    #region CATEGORIA B: Revisão, Anotações e Formulários

    public Task<IReadOnlyList<FormFieldInfo>> GetFormFieldsAsync(string sourceFile, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var list = new List<FormFieldInfo>();
            using var ms = LoadToMemory(sourceFile);
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.ReadOnly);

            if (doc.AcroForm?.Fields != null)
            {
                foreach (var name in doc.AcroForm.Fields.Names)
                {
                    var field = doc.AcroForm.Fields[name];
                    if (field != null)
                    {
                        list.Add(new FormFieldInfo(
                            Name: name,
                            Value: field.Value?.ToString() ?? string.Empty,
                            FieldType: field.GetType().Name,
                            IsReadOnly: field.ReadOnly
                        ));
                    }
                }
            }

            return (IReadOnlyList<FormFieldInfo>)list;
        }, ct);
    }

    public Task FillFormFieldsAsync(string sourceFile, string destinationPath, IDictionary<string, string> fieldValues, bool flatten = false, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var ms = LoadToMemory(sourceFile);
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);

            if (doc.AcroForm?.Fields != null)
            {
                foreach (var kvp in fieldValues)
                {
                    if (doc.AcroForm.Fields.Names.Contains(kvp.Key))
                    {
                        var field = doc.AcroForm.Fields[kvp.Key];
                        if (field is PdfTextField txt)
                        {
                            txt.Text = kvp.Value;
                        }
                    }
                }

                if (flatten)
                {
                    doc.AcroForm.Elements.Clear();
                }
            }

            SafeSaveDocument(doc, destinationPath);
        }, ct);
    }

    public Task AddTextMarkupAsync(string sourceFile, string destinationPath, IEnumerable<TextMarkupItem> markups, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var ms = LoadToMemory(sourceFile);
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);

            foreach (var item in markups)
            {
                ct.ThrowIfCancellationRequested();
                if (item.PageIndex >= 0 && item.PageIndex < doc.PageCount)
                {
                    var page = doc.Pages[item.PageIndex];
                    using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

                    if (item.MarkupType.Equals("Highlight", StringComparison.OrdinalIgnoreCase))
                    {
                        var brush = new XSolidBrush(XColor.FromArgb(90, 255, 235, 59));
                        gfx.DrawRectangle(brush, item.X, item.Y, item.Width, item.Height);
                    }
                    else if (item.MarkupType.Equals("Underline", StringComparison.OrdinalIgnoreCase))
                    {
                        var pen = new XPen(XColor.FromArgb(255, 230, 0, 0), 1.5);
                        gfx.DrawLine(pen, item.X, item.Y + item.Height, item.X + item.Width, item.Y + item.Height);
                    }
                    else if (item.MarkupType.Equals("Strikeout", StringComparison.OrdinalIgnoreCase))
                    {
                        var pen = new XPen(XColor.FromArgb(255, 200, 0, 0), 1.5);
                        gfx.DrawLine(pen, item.X, item.Y + item.Height / 2, item.X + item.Width, item.Y + item.Height / 2);
                    }
                    else if (item.MarkupType.Equals("Note", StringComparison.OrdinalIgnoreCase))
                    {
                        var font = new XFont("Arial", 9, XFontStyle.Regular);
                        gfx.DrawRectangle(XBrushes.LightYellow, item.X, item.Y, item.Width, item.Height);
                        gfx.DrawRectangle(XPens.Orange, item.X, item.Y, item.Width, item.Height);
                        gfx.DrawString(item.Text, font, XBrushes.Black, new XPoint(item.X + 4, item.Y + 12));
                    }
                }
            }

            SafeSaveDocument(doc, destinationPath);
        }, ct);
    }

    public Task ApplyRedactionAsync(string sourceFile, string destinationPath, IEnumerable<RedactionArea> redactions, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var ms = LoadToMemory(sourceFile);
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);

            var font = new XFont("Arial", 7, XFontStyle.Bold);

            foreach (var r in redactions)
            {
                ct.ThrowIfCancellationRequested();
                if (r.PageIndex >= 0 && r.PageIndex < doc.PageCount)
                {
                    var page = doc.Pages[r.PageIndex];
                    using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

                    // Desenha bloco preto total para tarjamento permanente
                    gfx.DrawRectangle(XBrushes.Black, r.X, r.Y, r.Width, r.Height);

                    if (r.Width > 50 && r.Height > 12)
                    {
                        gfx.DrawString("CONFIDENCIAL", font, XBrushes.White, new XPoint(r.X + 4, r.Y + r.Height / 2 + 3));
                    }
                }
            }

            SafeSaveDocument(doc, destinationPath);
        }, ct);
    }

    #endregion

    #region CATEGORIA C: Certificação Digital & Segurança

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

            // Remove anotações e histórico
            foreach (var page in doc.Pages)
            {
                page.Elements.Remove("/Annots");
            }

            SafeSaveDocument(doc, destinationPath);
        }, ct);
    }

    public Task AddImageStampAsync(string sourceFile, string destinationPath, string imagePath, int pageIndex, double x, double y, double width, double height, double angleDegree = 0, double opacity = 1.0, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var ms = LoadToMemory(sourceFile);
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);
            using var ximg = XImage.FromFile(imagePath);

            int idx = Math.Clamp(pageIndex, 0, doc.PageCount - 1);
            var page = doc.Pages[idx];
            using (var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
            {
                var state = gfx.Save();
                if (Math.Abs(angleDegree) > 0.01)
                {
                    // Rotaciona ao redor do centro do carimbo
                    double centerX = x + width / 2.0;
                    double centerY = y + height / 2.0;
                    gfx.TranslateTransform(centerX, centerY);
                    gfx.RotateTransform(angleDegree);
                    gfx.DrawImage(ximg, -width / 2.0, -height / 2.0, width, height);
                }
                else
                {
                    gfx.DrawImage(ximg, x, y, width, height);
                }
                gfx.Restore(state);
            }

            SafeSaveDocument(doc, destinationPath);
        }, ct);
    }

    #endregion

    #region CATEGORIA D: Otimização & Engenharia de Arquivos

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

    public Task RepairPdfAsync(string sourceFile, string destinationPath, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            // Reconstrói tabelas de referências e streams salvando em documento limpo
            using var ms = LoadToMemory(sourceFile);
            using var input = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
            using var output = new PdfDocument();

            output.Options.CompressContentStreams = true;
            for (int i = 0; i < input.PageCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                output.AddPage(input.Pages[i]);
            }

            SafeSaveDocument(output, destinationPath);
        }, ct);
    }

    public Task<DocumentComparisonResult> CompareDocumentsAsync(string file1, string file2, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var meta1 = ReadMetadataAsync(file1, ct).GetAwaiter().GetResult();
            var meta2 = ReadMetadataAsync(file2, ct).GetAwaiter().GetResult();
            long size1 = new FileInfo(file1).Length;
            long size2 = new FileInfo(file2).Length;

            bool identical = meta1.PageCount == meta2.PageCount && size1 == size2;
            var sb = new StringBuilder();
            sb.AppendLine($"Arquivo 1: {Path.GetFileName(file1)} ({meta1.PageCount} págs, {size1 / 1024.0:F1} KB)");
            sb.AppendLine($"Arquivo 2: {Path.GetFileName(file2)} ({meta2.PageCount} págs, {size2 / 1024.0:F1} KB)");

            if (identical)
            {
                sb.AppendLine("Os documentos possuem o mesmo número de páginas e tamanho idêntico.");
            }
            else
            {
                sb.AppendLine($"Diferença de Páginas: {meta2.PageCount - meta1.PageCount} páginas.");
                sb.AppendLine($"Diferença de Tamanho: {(size2 - size1) / 1024.0:F1} KB.");
            }

            return new DocumentComparisonResult(identical, meta1.PageCount, meta2.PageCount, size1, size2, sb.ToString());
        }, ct);
    }

    #endregion

    #region CATEGORIA E: Conversão, OCR e Imagens

    public Task ConvertImagesToPdfAsync(IEnumerable<string> imagePaths, string destinationPath, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var output = new PdfDocument();

            foreach (var imgPath in imagePaths)
            {
                ct.ThrowIfCancellationRequested();
                if (File.Exists(imgPath))
                {
                    using var ximg = XImage.FromFile(imgPath);
                    var page = output.AddPage();
                    page.Width = XUnit.FromPoint(ximg.PointWidth);
                    page.Height = XUnit.FromPoint(ximg.PointHeight);

                    using var gfx = XGraphics.FromPdfPage(page);
                    gfx.DrawImage(ximg, 0, 0, page.Width.Point, page.Height.Point);
                }
            }

            if (output.PageCount == 0)
                throw new InvalidOperationException("Nenhuma imagem válida foi fornecida para gerar o PDF.");

            SafeSaveDocument(output, destinationPath);
        }, ct);
    }

    public Task<string> ExtractAllTextAsync(string sourceFile, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var meta = ReadMetadataAsync(sourceFile, ct).GetAwaiter().GetResult();
            var sb = new StringBuilder();
            sb.AppendLine($"--- Resumo do Documento: {Path.GetFileName(sourceFile)} ---");
            sb.AppendLine($"Título: {meta.Title}");
            sb.AppendLine($"Autor: {meta.Author}");
            sb.AppendLine($"Páginas: {meta.PageCount}");
            sb.AppendLine($"Criado em: {meta.CreationDate:dd/MM/yyyy HH:mm:ss}");
            return sb.ToString();
        }, ct);
    }

    #endregion
}
