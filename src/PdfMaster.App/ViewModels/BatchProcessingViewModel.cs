using System;
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

    [ObservableProperty]
    private string _watermarkText = "CONFIDENCIAL";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _statusMessage = "Pronto para processar documentos PDF.";

    [ObservableProperty]
    private string? _lastOutputFilePath;

    public BatchProcessingViewModel(IPdfService pdfService, IDigitalSignatureService signatureService)
    {
        _pdfService = pdfService;
        _signatureService = signatureService;
    }

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
            foreach (var path in dialog.FileNames)
            {
                if (!Files.Any(f => f.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                {
                    Files.Add(new BatchFileItem { FullPath = path });
                }
            }
            StatusMessage = $"{Files.Count} documento(s) na lista.";
        }
    }

    [RelayCommand]
    private void RemoveSelectedFile()
    {
        if (SelectedFile != null)
        {
            Files.Remove(SelectedFile);
            SelectedFile = Files.FirstOrDefault();
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
    private async Task MergeFilesAsync()
    {
        if (Files.Count < 2)
        {
            MessageBox.Show("Adicione pelo menos 2 arquivos PDF para mesclar em um único arquivo.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var saveDialog = new SaveFileDialog
        {
            Title = "Salvar PDF Mesclado",
            Filter = "Arquivo PDF (*.pdf)|*.pdf",
            FileName = "PDF_Mesclado.pdf"
        };

        if (saveDialog.ShowDialog() != true) return;

        try
        {
            IsBusy = true;
            StatusMessage = "Mesclando documentos...";
            var progress = new Progress<double>(p => ProgressValue = p);

            var paths = Files.Select(f => f.FullPath).ToList();
            string result = await _pdfService.MergeFilesAsync(paths, saveDialog.FileName, progress);

            LastOutputFilePath = result;
            StatusMessage = "Documentos mesclados com sucesso!";
            MessageBox.Show($"PDF mesclado com sucesso!\nSalvo em: {result}", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
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
    private async Task CompressFilesAsync()
    {
        if (Files.Count == 0)
        {
            MessageBox.Show("Adicione ao menos um arquivo para comprimir.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var profile = new CompressionProfile(150, 75, true, true);
        IsBusy = true;
        StatusMessage = "Comprimindo e otimizando PDFs...";

        try
        {
            int count = 0;
            foreach (var item in Files)
            {
                item.Status = "Comprimindo...";
                string outPath = Path.Combine(
                    Path.GetDirectoryName(item.FullPath)!,
                    $"{Path.GetFileNameWithoutExtension(item.FullPath)}_otimizado.pdf"
                );

                await _pdfService.CompressPdfAsync(item.FullPath, outPath, profile);
                item.Status = "Concluído";
                count++;
                ProgressValue = (double)count / Files.Count * 100.0;
                LastOutputFilePath = outPath;
            }

            StatusMessage = $"{count} arquivo(s) comprimido(s) com sucesso!";
            MessageBox.Show($"Otimização concluída para {count} arquivo(s)!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show("Selecione arquivos para adicionar marca d'água.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(WatermarkText))
        {
            MessageBox.Show("Digite o texto da marca d'água.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        StatusMessage = "Aplicando marca d'água...";

        try
        {
            int count = 0;
            foreach (var item in Files)
            {
                item.Status = "Aplicando marca...";
                string outPath = Path.Combine(
                    Path.GetDirectoryName(item.FullPath)!,
                    $"{Path.GetFileNameWithoutExtension(item.FullPath)}_com_marca.pdf"
                );

                await _pdfService.AddWatermarkAsync(item.FullPath, outPath, WatermarkText, 0.25, 45);
                item.Status = "Marca aplicada";
                count++;
                ProgressValue = (double)count / Files.Count * 100.0;
                LastOutputFilePath = outPath;
            }

            StatusMessage = $"Marca d'água inserida em {count} arquivo(s)!";
            MessageBox.Show($"Marca d'água aplicada com sucesso!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
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
    private async Task ProtectWithPasswordAsync()
    {
        if (Files.Count == 0)
        {
            MessageBox.Show("Adicione arquivos para proteger.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrEmpty(Password))
        {
            MessageBox.Show("Informe uma senha no campo correspondente.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        StatusMessage = "Criptografando documentos com senha...";

        try
        {
            int count = 0;
            foreach (var item in Files)
            {
                item.Status = "Protegendo...";
                string outPath = Path.Combine(
                    Path.GetDirectoryName(item.FullPath)!,
                    $"{Path.GetFileNameWithoutExtension(item.FullPath)}_protegido.pdf"
                );

                await _pdfService.ProtectPdfAsync(item.FullPath, outPath, Password, Password);
                item.Status = "Protegido";
                count++;
                ProgressValue = (double)count / Files.Count * 100.0;
                LastOutputFilePath = outPath;
            }

            StatusMessage = $"{count} arquivo(s) criptografado(s) com sucesso!";
            MessageBox.Show("Documentos protegidos com senha criptográfica!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
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
    private void OpenLastOutputFile()
    {
        if (!string.IsNullOrEmpty(LastOutputFilePath) && File.Exists(LastOutputFilePath))
        {
            Process.Start(new ProcessStartInfo(LastOutputFilePath) { UseShellExecute = true });
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
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }
        }
    }
}
