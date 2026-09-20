using System.Windows;
using PdfMaster.App.ViewModels;

namespace PdfMaster.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
