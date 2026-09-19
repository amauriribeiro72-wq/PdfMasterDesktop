using PdfMaster.Core.Models;

namespace PdfMaster.Core.Interfaces;

public interface IPdfService
{
    // Manipulação de Páginas
    Task<string> MergeFilesAsync(IEnumerable<string> sourceFiles, string destinationPath, IProgress<double>? progress = null, CancellationToken ct = default);
    Task<IEnumerable<string>> SplitAsync(string sourceFile, string outputFolder, IEnumerable<PageRange> ranges, CancellationToken ct = default);
    Task ReorderPagesAsync(string sourceFile, string destinationPath, IEnumerable<int> newPageIndices, CancellationToken ct = default);
    Task RotatePagesAsync(string sourceFile, string destinationPath, IDictionary<int, int> pageRotations, CancellationToken ct = default);
    Task DeletePagesAsync(string sourceFile, string destinationPath, IEnumerable<int> pagesToDelete, CancellationToken ct = default);
    Task ExtractPagesAsync(string sourceFile, string destinationPath, IEnumerable<int> pagesToExtract, CancellationToken ct = default);
    Task AddWatermarkAsync(string sourceFile, string destinationPath, string watermarkText, double opacity = 0.3, double angleDegree = 45, CancellationToken ct = default);
    Task AddPageNumbersAsync(string sourceFile, string destinationPath, string formatMask = "Pág. {0} de {1}", bool isHeader = false, CancellationToken ct = default);
    
    // Otimização, Metadados e Segurança
    Task<long> CompressPdfAsync(string sourceFile, string destinationPath, CompressionProfile profile, IProgress<double>? progress = null, CancellationToken ct = default);
    Task<DocumentMetadata> ReadMetadataAsync(string sourceFile, CancellationToken ct = default);
    Task SanitizeMetadataAsync(string sourceFile, string destinationPath, CancellationToken ct = default);
    Task ProtectPdfAsync(string sourceFile, string destinationPath, string userPassword, string ownerPassword, CancellationToken ct = default);
    Task UnlockPdfAsync(string sourceFile, string destinationPath, string password, CancellationToken ct = default);
}
