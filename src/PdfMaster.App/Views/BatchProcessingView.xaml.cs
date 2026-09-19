using System.Windows.Controls;
using PdfMaster.App.ViewModels;

namespace PdfMaster.App.Views;

public partial class BatchProcessingView : UserControl
{
    public BatchProcessingView(BatchProcessingViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
