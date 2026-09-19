namespace PdfMaster.Core.Models;

public record PageRange(int Start, int End);

public record PageCropMargins(double Top, double Bottom, double Left, double Right);

public record CompressionProfile(int Dpi, int ImageQuality, bool RemoveMetadata, bool DownsampleImages);

public record DocumentMetadata(
    string? Title,
    string? Author,
    string? Subject,
    string? Keywords,
    string? Creator,
    string? Producer,
    DateTime? CreationDate,
    DateTime? ModificationDate,
    int PageCount
);

public record OcrWordBox(string Text, double X, double Y, double Width, double Height);

public record OcrPageResult(int PageIndex, string FullText, IReadOnlyList<OcrWordBox> Words);

public record SignatureStampPosition(int PageNumber, float X, float Y, float Width, float Height);

public record DigitalCertificateInfo(
    string Subject,
    string Issuer,
    string Thumbprint,
    DateTime NotBefore,
    DateTime NotAfter,
    bool HasPrivateKey
);
