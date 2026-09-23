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

public record PageThumbnail(int PageIndex, int PageNumber, byte[] ImageBytes, double Width, double Height);

public record RedactionArea(int PageIndex, double X, double Y, double Width, double Height);

public record NUpConfiguration(int PagesPerSheet, bool DrawBorder = true, bool Landscape = true);

public record FormFieldInfo(string Name, string Value, string FieldType, bool IsReadOnly);

public record DocumentComparisonResult(bool Identical, int PageCount1, int PageCount2, long Size1, long Size2, string Summary);

public record TextMarkupItem(int PageIndex, string Text, string MarkupType, double X, double Y, double Width, double Height, string? ColorHex);
