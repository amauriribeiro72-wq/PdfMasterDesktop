using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PdfMaster.App.ViewModels;

namespace PdfMaster.App.Views;

public partial class BatchProcessingView : UserControl
{
    public BatchProcessingView()
    {
        InitializeComponent();
    }

    public BatchProcessingView(BatchProcessingViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void OnListDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnListDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var dropped = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (dropped != null && dropped.Length > 0)
            {
                var pdfFiles = dropped.Where(f => Path.GetExtension(f).Equals(".pdf", System.StringComparison.OrdinalIgnoreCase)).ToList();
                if (pdfFiles.Count > 0 && DataContext is BatchProcessingViewModel vm)
                {
                    vm.AddFilePaths(pdfFiles);
                }
            }
        }
    }
}
