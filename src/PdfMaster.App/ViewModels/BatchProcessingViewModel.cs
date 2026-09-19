using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PdfMaster.Core.Interfaces;
using PdfMaster.Core.Models;

namespace PdfMaster.App.ViewModels;

public class BatchFileItem : ObservableObject
{
    public string FullPath { get; set; } = string.Empty;
    public string FileName => Path.GetFileName(FullPath);
    public string FileFolder => Path.GetDirectoryName(FullPath) ?? string.Empty;
    
    public string FileSizeFormatted
    {
        get
        {
            if (!File.Exists(FullPath)) return "-";
            long bytes = new FileInfo(FullPath).Length;
            if (bytes > 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F2} MB";
            return $"{bytes / 1024.0:F1} KB";
        }
    }

    private string _status = "Pronto";
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
}

public partial class BatchProcessingViewModel : ObservableObject
{
    private readonly IPdfService _pdfService;
    private readonly IDigitalSignatureService _signatureService;

    [ObservableProperty]
    private ObservableCollection<BatchFileItem> _files = new();

    [ObservableProperty]
    private BatchFileItem? _selectedFile;

    // Configurações de Operações
    [ObservableProperty]
    private string _watermarkText = "CONFIDENCIAL";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string _unlockPassword = "";

    [ObservableProperty]
    private string _splitRangesText = "1-2, 3-5";

    [ObservableProperty]
    private string _pagesToDeleteText = "1";

    [ObservableProperty]
    private string _pagesToExtractText = "1-3";

    [ObservableProperty]
    private int _rotationAngle = 90;

    [ObservableProperty]
    private string _pageNumberMask = "Pág. {0} de {1}";

    [ObservableProperty]
    private bool _pageNumberInHeader = false;

    // Assinatura Digital ICP-Brasil
    [ObservableProperty]
    private ObservableCollection<DigitalCertificateInfo> _availableCertificates = new();

    [ObservableProperty]
    private DigitalCertificateInfo? _selectedCertificate;

    [ObservableProperty]
    private string _signatureReason = "Documento assinado digitalmente";

    // Estado da Execução
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _statusMessage = "Pronto para processar documentos PDF.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastOutputFile))]
    private string? _lastOutputFilePath;

    public bool HasLastOutputFile => !string.IsNullOrEmpty(LastOutputFilePath) && File.Exists(LastOutputFilePath);

    public BatchProcessingViewModel(IPdfService pdfService, IDigitalSignatureService signatureService)
    {
        _pdfService = pdfService;
        _signatureService = signatureService;
        LoadCertificates();
    }

    #region Gerenciamento da Lista de Arquivos

    [RelayCommand]
    private void AddFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecionar Arquivos PDF",
            Filter = "Arquivos PDF (*.pdf)|*.pdf|Todos os Arquivos (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            AddFilePaths(dialog.FileNames);
        }
    }

    public void AddFilePaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase) &&
                !Files.Any(f => f.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                Files.Add(new BatchFileItem { FullPath = path });
            }
        }
        if (SelectedFile == null && Files.Count > 0)
        {
            SelectedFile = Files[0];
        }
        StatusMessage = $"{Files.Count} documento(s) na lista.";
    }

    [RelayCommand]
    private void RemoveSelectedFile()
    {
        if (SelectedFile != null)
        {
            int index = Files.IndexOf(SelectedFile);
            Files.Remove(SelectedFile);
            SelectedFile = Files.Count > index ? Files[index] : Files.LastOrDefault();
            StatusMessage = $"{Files.Count} documento(s) na lista.";
        }
    }

    [RelayCommand]
    private void ClearAllFiles()
    {
        Files.Clear();
        SelectedFile = null;
        StatusMessage = "Lista de arquivos limpa.";
        LastOutputFilePath = null;
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedFile != null)
        {
            int index = Files.IndexOf(SelectedFile);
            if (index > 0)
            {
                Files.Move(index, index - 1);
            }
        }
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedFile != null)
        {
            int index = Files.IndexOf(SelectedFile);
            if (index >= 0 && index < Files.Count - 1)
            {
                Files.Move(index, index + 1);
            }
        }
    }

    #endregion

    #region Operações Salvas Diretamente NA MESMA PASTA

    [RelayCommand]
    private async Task MergeFilesAsync()
    {
        if (Files.Count < 2)
        {
            MessageBox.Show("Adicione pelo menos 2 arquivos PDF na lista para mesclá-los.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Mesclando documentos...";
            var progress = new Progress<double>(p => ProgressValue = p);

            // Salva na MESMA PASTA do primeiro arquivo
            string firstFolder = Path.GetDirectoryName(Files[0].FullPath)!;
            string firstBaseName = Path.GetFileNameWithoutExtension(Files[0].FullPath);
            string destination = Path.Combine(firstFolder, $"{firstBaseName}_mesclado.pdf");

            var paths = Files.Select(f => f.FullPath).ToList();
            string result = await _pdfService.MergeFilesAsync(paths, destination, progress);

            LastOutputFilePath = result;
            StatusMessage = $"PDF mesclado com sucesso na mesma pasta!";
            NotifyCompletion(result);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro: {ex.Message}";
            MessageBox.Show($"Falha ao mesclar: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SplitFilesAsync()
    {
        var targetFile = GetTargetFile();
        if (targetFile == null) return;

        var ranges = ParseRanges(SplitRangesText);
        if (ranges.Count == 0)
        {
            MessageBox.Show("Informe os intervalos de páginas válidos (ex: 1-2, 3-5).", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Dividindo arquivo PDF...";
            string folder = Path.GetDirectoryName(targetFile.FullPath)!;

            var results = await _pdfService.SplitAsync(targetFile.FullPath, folder, ranges);
            var resultList = results.ToList();

            if (resultList.Count > 0)
            {
                LastOutputFilePath = resultList[0];
                StatusMessage = $"{resultList.Count} partes geradas na mesma pasta do arquivo!";
                NotifyCompletion(resultList[0], $"{resultList.Count} arquivos gerados na mesma pasta!");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro: {ex.Message}";
            MessageBox.Show($"Falha ao dividir: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Rotate90Async() => await RotateWithAngleAsync(90);

    [RelayCommand]
    private async Task Rotate180Async() => await RotateWithAngleAsync(180);

    [RelayCommand]
    private async Task Rotate270Async() => await RotateWithAngleAsync(270);

    private async Task RotateWithAngleAsync(int angle)
    {
        var targetFile = GetTargetFile();
        if (targetFile == null) return;

        try
        {
            IsBusy = true;
            RotationAngle = angle;
            StatusMessage = $"Girando páginas em {angle}°...";

            string folder = Path.GetDirectoryName(targetFile.FullPath)!;
            string outPath = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(targetFile.FullPath)}_girado_{angle}.pdf");

            var meta = await _pdfService.ReadMetadataAsync(targetFile.FullPath);
            var rotations = new Dictionary<int, int>();
            for (int i = 0; i < meta.PageCount; i++)
            {
                rotations[i] = angle;
            }

            await _pdfService.RotatePagesAsync(targetFile.FullPath, outPath, rotations);
            LastOutputFilePath = outPath;
            StatusMessage = $"Páginas rotacionadas em {angle}° salvas na mesma pasta!";
            NotifyCompletion(outPath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro: {ex.Message}";
            MessageBox.Show($"Erro ao girar: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeletePagesAsync()
    {
        var targetFile = GetTargetFile();
        if (targetFile == null) return;

        var pageIndices = ParsePagesToZeroBased(PagesToDeleteText);
        if (pageIndices.Count == 0)
        {
            MessageBox.Show("Informe as páginas que deseja excluir (ex: 1, 3, 5-8).", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Excluindo páginas...";

            string folder = Path.GetDirectoryName(targetFile.FullPath)!;
            string outPath = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(targetFile.FullPath)}_paginas_removidas.pdf");

            await _pdfService.DeletePagesAsync(targetFile.FullPath, outPath, pageIndices);
            LastOutputFilePath = outPath;
            StatusMessage = "Páginas removidas com sucesso na mesma pasta!";
            NotifyCompletion(outPath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro: {ex.Message}";
            MessageBox.Show($"Erro ao excluir páginas: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExtractPagesAsync()
    {
        var targetFile = GetTargetFile();
        if (targetFile == null) return;

        var pageIndices = ParsePagesToZeroBased(PagesToExtractText);
        if (pageIndices.Count == 0)
        {
            MessageBox.Show("Informe as páginas que deseja extrair (ex: 1, 3, 5-8).", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Extraindo páginas...";

            string folder = Path.GetDirectoryName(targetFile.FullPath)!;
            string outPath = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(targetFile.FullPath)}_extraido.pdf");

            await _pdfService.ExtractPagesAsync(targetFile.FullPath, outPath, pageIndices);
            LastOutputFilePath = outPath;
            StatusMessage = "Páginas extraídas com sucesso na mesma pasta!";
            NotifyCompletion(outPath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro: {ex.Message}";
            MessageBox.Show($"Erro ao extrair páginas: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CompressFilesAsync()
    {
        if (Files.Count == 0)
        {
            MessageBox.Show("Adicione arquivos para comprimir.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var profile = new CompressionProfile(150, 75, true, true);
        IsBusy = true;
        StatusMessage = "Comprimindo e reduzindo tamanho...";

        try
        {
            int count = 0;
            string lastOut = string.Empty;
            foreach (var item in Files)
            {
                item.Status = "Comprimindo...";
                string folder = Path.GetDirectoryName(item.FullPath)!;
                string outPath = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(item.FullPath)}_comprimido.pdf");

                await _pdfService.CompressPdfAsync(item.FullPath, outPath, profile);
                item.Status = "Concluído";
                count++;
                lastOut = outPath;
                ProgressValue = (double)count / Files.Count * 100.0;
            }

            LastOutputFilePath = lastOut;
            StatusMessage = $"{count} arquivo(s) comprimido(s) com sucesso na mesma pasta!";
            NotifyCompletion(lastOut);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro: {ex.Message}";
            MessageBox.Show($"Erro na compressão: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ApplyWatermarkAsync()
    {
        if (Files.Count == 0)
        {
            MessageBox.Show("Adicione arquivos para inserir marca d'água.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(WatermarkText))
        {
            MessageBox.Show("Digite o texto da marca d'água.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        StatusMessage = "Inserindo marca d'água...";

        try
        {
            int count = 0;
            string lastOut = string.Empty;
            foreach (var item in Files)
            {
                item.Status = "Aplicando marca...";
                string folder = Path.GetDirectoryName(item.FullPath)!;
                string outPath = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(item.FullPath)}_marca_dagua.pdf");

                await _pdfService.AddWatermarkAsync(item.FullPath, outPath, WatermarkText, 0.25, 45);
                item.Status = "Marca inserida";
                count++;
                lastOut = outPath;
                ProgressValue = (double)count / Files.Count * 100.0;
            }

            LastOutputFilePath = lastOut;
            StatusMessage = $"Marca d'água aplicada em {count} arquivo(s) na mesma pasta!";
            NotifyCompletion(lastOut);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro: {ex.Message}";
            MessageBox.Show($"Erro: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddPageNumbersAsync()
    {
        var targetFile = GetTargetFile();
        if (targetFile == null) return;

        try
        {
            IsBusy = true;
            StatusMessage = "Inserindo numeração de páginas...";

            string folder = Path.GetDirectoryName(targetFile.FullPath)!;
            string outPath = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(targetFile.FullPath)}_numerado.pdf");

            await _pdfService.AddPageNumbersAsync(targetFile.FullPath, outPath, PageNumberMask, PageNumberInHeader);
            LastOutputFilePath = outPath;
            StatusMessage = "Numeração aplicada com sucesso na mesma pasta!";
            NotifyCompletion(outPath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro: {ex.Message}";
            MessageBox.Show($"Erro na paginação: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ProtectWithPasswordAsync()
    {
        if (Files.Count == 0)
        {
            MessageBox.Show("Adicione arquivos para proteger.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrEmpty(Password))
        {
            MessageBox.Show("Informe a senha desejada.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        StatusMessage = "Criptografando com senha...";

        try
        {
            int count = 0;
            string lastOut = string.Empty;
            foreach (var item in Files)
            {
                item.Status = "Protegendo...";
                string folder = Path.GetDirectoryName(item.FullPath)!;
                string outPath = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(item.FullPath)}_protegido.pdf");

                await _pdfService.ProtectPdfAsync(item.FullPath, outPath, Password, Password);
                item.Status = "Protegido";
                count++;
                lastOut = outPath;
                ProgressValue = (double)count / Files.Count * 100.0;
            }

            LastOutputFilePath = lastOut;
            StatusMessage = $"{count} documento(s) protegido(s) com sucesso na mesma pasta!";
            NotifyCompletion(lastOut);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro: {ex.Message}";
            MessageBox.Show($"Erro ao proteger: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UnlockWithPasswordAsync()
    {
        var targetFile = GetTargetFile();
        if (targetFile == null) return;

        if (string.IsNullOrEmpty(UnlockPassword))
        {
            MessageBox.Show("Informe a senha atual do arquivo para desbloqueá-lo.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Removendo senha de proteção...";

            string folder = Path.GetDirectoryName(targetFile.FullPath)!;
            string outPath = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(targetFile.FullPath)}_desbloqueado.pdf");

            await _pdfService.UnlockPdfAsync(targetFile.FullPath, outPath, UnlockPassword);
            LastOutputFilePath = outPath;
            StatusMessage = "Senha removida com sucesso! Arquivo desbloqueado salvo na mesma pasta.";
            NotifyCompletion(outPath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro: {ex.Message}";
            MessageBox.Show($"Erro ao desbloquear: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SanitizeMetadataAsync()
    {
        if (Files.Count == 0)
        {
            MessageBox.Show("Adicione arquivos para sanitizar metadados.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        StatusMessage = "Limpando metadados e histórico (LGPD)...";

        try
        {
            int count = 0;
            string lastOut = string.Empty;
            foreach (var item in Files)
            {
                item.Status = "Sanitizando...";
                string folder = Path.GetDirectoryName(item.FullPath)!;
                string outPath = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(item.FullPath)}_sanitizado.pdf");

                await _pdfService.SanitizeMetadataAsync(item.FullPath, outPath);
                item.Status = "Sanitizado";
                count++;
                lastOut = outPath;
                ProgressValue = (double)count / Files.Count * 100.0;
            }

            LastOutputFilePath = lastOut;
            StatusMessage = $"Metadados limpos de {count} arquivo(s) na mesma pasta!";
            NotifyCompletion(lastOut);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro: {ex.Message}";
            MessageBox.Show($"Erro: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SignDocumentAsync()
    {
        var targetFile = GetTargetFile();
        if (targetFile == null) return;

        if (SelectedCertificate == null)
        {
            MessageBox.Show("Nenhum certificado digital selecionado. Conecte seu token A3 ou instale seu certificado A1 no Windows.", "Certificado Requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Assinando documento com ICP-Brasil...";

            string folder = Path.GetDirectoryName(targetFile.FullPath)!;
            string outPath = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(targetFile.FullPath)}_assinado.pdf");

            await _signatureService.SignPdfAsync(targetFile.FullPath, outPath, SelectedCertificate, reason: SignatureReason);
            LastOutputFilePath = outPath;
            StatusMessage = "Documento assinado digitalmente com sucesso na mesma pasta!";
            NotifyCompletion(outPath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro: {ex.Message}";
            MessageBox.Show($"Erro ao assinar documento: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void LoadCertificates()
    {
        try
        {
            AvailableCertificates.Clear();
            var certs = _signatureService.GetAvailableCertificatesAsync().GetAwaiter().GetResult();
            foreach (var cert in certs)
            {
                AvailableCertificates.Add(cert);
            }
            SelectedCertificate = AvailableCertificates.FirstOrDefault();
        }
        catch
        {
            // Repositório de certificados sem itens
        }
    }

    #endregion

    #region Ações de Saída e Notificação Pós-Processamento

    private void NotifyCompletion(string outputPath, string? customMessage = null)
    {
        string message = customMessage ?? $"Operação concluída com sucesso!\n\nArquivo salvo na mesma pasta:\n{outputPath}";
        var result = MessageBox.Show(
            $"{message}\n\nDeseja abrir o arquivo agora?",
            "Concluído com Sucesso",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (result == MessageBoxResult.Yes)
        {
            OpenLastOutputFile();
        }
    }

    [RelayCommand]
    private void OpenLastOutputFile()
    {
        if (!string.IsNullOrEmpty(LastOutputFilePath) && File.Exists(LastOutputFilePath))
        {
            try
            {
                Process.Start(new ProcessStartInfo(LastOutputFilePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Não foi possível abrir o arquivo: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        if (!string.IsNullOrEmpty(LastOutputFilePath))
        {
            string? folder = Path.GetDirectoryName(LastOutputFilePath);
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Não foi possível abrir a pasta: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private BatchFileItem? GetTargetFile()
    {
        var target = SelectedFile ?? Files.FirstOrDefault();
        if (target == null)
        {
            MessageBox.Show("Selecione um arquivo PDF na lista à esquerda para realizar esta ação.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        return target;
    }

    private static List<PageRange> ParseRanges(string text)
    {
        var result = new List<PageRange>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        var parts = text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Contains('-'))
            {
                var sides = trimmed.Split('-');
                if (int.TryParse(sides[0], out int start) && int.TryParse(sides[1], out int end))
                {
                    result.Add(new PageRange(start, end));
                }
            }
            else if (int.TryParse(trimmed, out int single))
            {
                result.Add(new PageRange(single, single));
            }
        }
        return result;
    }

    private static List<int> ParsePagesToZeroBased(string text)
    {
        var result = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(text)) return result.ToList();

        var parts = text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Contains('-'))
            {
                var sides = trimmed.Split('-');
                if (int.TryParse(sides[0], out int start) && int.TryParse(sides[1], out int end))
                {
                    for (int i = Math.Min(start, end); i <= Math.Max(start, end); i++)
                    {
                        if (i > 0) result.Add(i - 1); // Converte para zero-based
                    }
                }
            }
            else if (int.TryParse(trimmed, out int single))
            {
                if (single > 0) result.Add(single - 1);
            }
        }
        return result.OrderBy(x => x).ToList();
    }

    #endregion
}
