using Wpf.Ui.Controls;
using PdfMaster.App.ViewModels;

namespace PdfMaster.App.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
