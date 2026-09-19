using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using PdfMaster.Core.Interfaces;
using PdfMaster.Core.Models;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace PdfMaster.Infrastructure.Services;

public class WindowsSignatureService : IDigitalSignatureService
{
    public Task<IReadOnlyList<DigitalCertificateInfo>> GetAvailableCertificatesAsync()
    {
        return Task.Run(() =>
        {
            var list = new List<DigitalCertificateInfo>();

            try
            {
                using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly);

                var now = DateTime.Now;
                foreach (var cert in store.Certificates)
                {
                    if (cert.HasPrivateKey && cert.NotBefore <= now && cert.NotAfter >= now)
                    {
                        list.Add(new DigitalCertificateInfo(
                            Subject: cert.Subject,
                            Issuer: cert.Issuer,
                            Thumbprint: cert.Thumbprint,
                            NotBefore: cert.NotBefore,
                            NotAfter: cert.NotAfter,
                            HasPrivateKey: cert.HasPrivateKey
                        ));
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Não foi possível ler os certificados do Windows: {ex.Message}", ex);
            }

            return (IReadOnlyList<DigitalCertificateInfo>)list;
        });
    }

    public Task SignPdfAsync(
        string sourcePdfPath,
        string destinationPdfPath,
        DigitalCertificateInfo certificate,
        SignatureStampPosition? stampPosition = null,
        string reason = "Documento assinado digitalmente",
        string location = "Brasil",
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var fullDest = Path.GetFullPath(destinationPdfPath);
            var dir = Path.GetDirectoryName(fullDest);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Lê na memória para evitar bloqueios de arquivo
            byte[] sourceBytes = File.ReadAllBytes(sourcePdfPath);
            using var ms = new MemoryStream(sourceBytes);
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);

            int targetPageIdx = (stampPosition != null && stampPosition.PageNumber > 0 && stampPosition.PageNumber <= doc.PageCount)
                ? stampPosition.PageNumber - 1
                : doc.PageCount - 1;

            var page = doc.Pages[targetPageIdx];
            using (var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
            {
                float x = stampPosition?.X ?? 40;
                float y = stampPosition?.Y ?? (float)(page.Height.Point - 120);
                float w = stampPosition?.Width ?? 320;
                float h = stampPosition?.Height ?? 70;

                // Moldura com padrão visual PAdES
                var pen = new XPen(XColor.FromArgb(200, 0, 90, 180), 1.5);
                var bgBrush = new XSolidBrush(XColor.FromArgb(245, 248, 252, 255));
                gfx.DrawRectangle(pen, bgBrush, x, y, w, h);

                var boldFont = new XFont("Arial", 8, XFontStyle.Bold);
                var textFont = new XFont("Arial", 7, XFontStyle.Regular);
                var textBrush = new XSolidBrush(XColor.FromArgb(255, 30, 30, 30));

                gfx.DrawString("DOCUMENTO ASSINADO DIGITALMENTE (ICP-BRASIL)", boldFont, textBrush, new XPoint(x + 10, y + 16));
                
                string signerName = certificate.Subject.Split(',').FirstOrDefault(p => p.Trim().StartsWith("CN="))?.Replace("CN=", "") ?? certificate.Subject;
                if (signerName.Length > 42) signerName = signerName.Substring(0, 39) + "...";

                gfx.DrawString($"Signatário: {signerName}", textFont, textBrush, new XPoint(x + 10, y + 30));
                gfx.DrawString($"Data: {DateTime.Now:dd/MM/yyyy HH:mm:ss} (Horário de Brasília)", textFont, textBrush, new XPoint(x + 10, y + 44));
                gfx.DrawString($"Finalidade: {reason} | {location}", textFont, textBrush, new XPoint(x + 10, y + 58));
            }

            // Salva de forma atômica
            string tempFile = Path.Combine(dir ?? ".", $"{Path.GetFileName(fullDest)}.tmp_{Guid.NewGuid():N}");
            try
            {
                doc.Save(tempFile);
                File.Move(tempFile, fullDest, overwrite: true);
            }
            catch (Exception ex)
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
                throw new IOException($"Erro ao gravar arquivo assinado '{Path.GetFileName(fullDest)}': {ex.Message}", ex);
            }

            // Gera assinatura CMS destacada (.p7s)
            try
            {
                using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly);

                var certs = store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, validOnly: false);
                if (certs.Count > 0)
                {
                    var x509Cert = certs[0];
                    byte[] fileBytes = File.ReadAllBytes(fullDest);
                    var contentInfo = new ContentInfo(fileBytes);
                    var signedCms = new SignedCms(contentInfo, detached: true);
                    var cmsSigner = new CmsSigner(x509Cert);
                    signedCms.ComputeSignature(cmsSigner);
                    byte[] signatureBytes = signedCms.Encode();

                    string p7sFile = Path.ChangeExtension(fullDest, ".p7s");
                    File.WriteAllBytes(p7sFile, signatureBytes);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"O selo foi aplicado, mas a assinatura criptográfica falhou: {ex.Message}", ex);
            }
        }, ct);
    }
}
