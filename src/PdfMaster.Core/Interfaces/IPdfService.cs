using PdfMaster.Core.Models;

namespace PdfMaster.Core.Interfaces;

public interface IPdfService
{
    // CATEGORIA A: Manipulação Avançada de Páginas
    Task<string> MergeFilesAsync(IEnumerable<string> sourceFiles, string destinationPath, IProgress<double>? progress = null, CancellationToken ct = default);
    Task<IEnumerable<string>> SplitAsync(string sourceFile, string outputFolder, IEnumerable<PageRange> ranges, CancellationToken ct = default);
    Task ReorderPagesAsync(string sourceFile, string destinationPath, IEnumerable<int> newPageIndices, CancellationToken ct = default);
    Task RotatePagesAsync(string sourceFile, string destinationPath, IDictionary<int, int> pageRotations, CancellationToken ct = default);
    Task DeletePagesAsync(string sourceFile, string destinationPath, IEnumerable<int> pagesToDelete, CancellationToken ct = default);
    Task ExtractPagesAsync(string sourceFile, string destinationPath, IEnumerable<int> pagesToExtract, CancellationToken ct = default);
    Task CropPagesAsync(string sourceFile, string destinationPath, PageCropMargins margins, CancellationToken ct = default);
    Task GenerateNUpAsync(string sourceFile, string destinationPath, NUpConfiguration config, CancellationToken ct = default);
    Task AddWatermarkAsync(string sourceFile, string destinationPath, string watermarkText, double opacity = 0.3, double angleDegree = 45, CancellationToken ct = default);
    Task AddWatermarkImageAsync(string sourceFile, string destinationPath, string imagePath, double opacity = 0.3, CancellationToken ct = default);
    Task AddPageNumbersAsync(string sourceFile, string destinationPath, string formatMask = "Pág. {0} de {1}", bool isHeader = false, CancellationToken ct = default);
    Task FlattenDocumentAsync(string sourceFile, string destinationPath, CancellationToken ct = default);

    // CATEGORIA B: Revisão, Anotações e Formulários
    Task<IReadOnlyList<FormFieldInfo>> GetFormFieldsAsync(string sourceFile, CancellationToken ct = default);
    Task FillFormFieldsAsync(string sourceFile, string destinationPath, IDictionary<string, string> fieldValues, bool flatten = false, CancellationToken ct = default);
    Task AddTextMarkupAsync(string sourceFile, string destinationPath, IEnumerable<TextMarkupItem> markups, CancellationToken ct = default);
    Task ApplyRedactionAsync(string sourceFile, string destinationPath, IEnumerable<RedactionArea> redactions, CancellationToken ct = default);

    // CATEGORIA C: Certificação Digital & Segurança
    Task ProtectPdfAsync(string sourceFile, string destinationPath, string userPassword, string ownerPassword, CancellationToken ct = default);
    Task UnlockPdfAsync(string sourceFile, string destinationPath, string password, CancellationToken ct = default);
    Task SanitizeMetadataAsync(string sourceFile, string destinationPath, CancellationToken ct = default);
    Task AddImageStampAsync(string sourceFile, string destinationPath, string imagePath, int pageIndex, double x, double y, double width, double height, double angleDegree = 0, double opacity = 1.0, CancellationToken ct = default);

    // CATEGORIA D: Otimização & Engenharia de Arquivos
    Task<long> CompressPdfAsync(string sourceFile, string destinationPath, CompressionProfile profile, IProgress<double>? progress = null, CancellationToken ct = default);
    Task<DocumentMetadata> ReadMetadataAsync(string sourceFile, CancellationToken ct = default);
    Task RepairPdfAsync(string sourceFile, string destinationPath, CancellationToken ct = default);
    Task<DocumentComparisonResult> CompareDocumentsAsync(string file1, string file2, CancellationToken ct = default);

    // CATEGORIA E: Conversão, OCR e Imagens
    Task ConvertImagesToPdfAsync(IEnumerable<string> imagePaths, string destinationPath, CancellationToken ct = default);
    Task<string> ExtractAllTextAsync(string sourceFile, CancellationToken ct = default);
}
