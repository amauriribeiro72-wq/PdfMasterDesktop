using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PdfMaster.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "PDF Master Pro - Desktop Edition";

    [ObservableProperty]
    private BatchProcessingViewModel _batchProcessing;

    public MainWindowViewModel(BatchProcessingViewModel batchProcessing)
    {
        _batchProcessing = batchProcessing;
    }
}
