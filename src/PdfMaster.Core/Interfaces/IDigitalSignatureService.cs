using PdfMaster.Core.Models;

namespace PdfMaster.Core.Interfaces;

public interface IDigitalSignatureService
{
    Task<IReadOnlyList<DigitalCertificateInfo>> GetAvailableCertificatesAsync();
    Task SignPdfAsync(
        string sourcePdfPath,
        string destinationPdfPath,
        DigitalCertificateInfo certificate,
        SignatureStampPosition? stampPosition = null,
        string reason = "Documento assinado digitalmente",
        string location = "Brasil",
        CancellationToken ct = default);
}
