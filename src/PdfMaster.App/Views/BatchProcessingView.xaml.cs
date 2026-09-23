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

    private bool _isDraggingStamp;
    private bool _isResizingStamp;
    private Point _dragStartPoint;
    private double _initialStampX;
    private double _initialStampY;
    private double _initialStampW;
    private double _initialStampH;

    private void Stamp_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is BatchProcessingViewModel vm && sender is UIElement elem)
        {
            _isDraggingStamp = true;
            _dragStartPoint = e.GetPosition(StampCanvas);
            _initialStampX = vm.StampX;
            _initialStampY = vm.StampY;
            elem.CaptureMouse();
            e.Handled = true;
        }
    }

    private void Stamp_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDraggingStamp && DataContext is BatchProcessingViewModel vm)
        {
            var cur = e.GetPosition(StampCanvas);
            double deltaX = cur.X - _dragStartPoint.X;
            double deltaY = cur.Y - _dragStartPoint.Y;

            double maxW = StampCanvas.ActualWidth > 0 ? StampCanvas.ActualWidth : vm.CanvasRenderWidth;
            double maxH = StampCanvas.ActualHeight > 0 ? StampCanvas.ActualHeight : vm.CanvasRenderHeight;

            double newX = Math.Max(0, Math.Min(maxW - vm.StampWidth, _initialStampX + deltaX));
            double newY = Math.Max(0, Math.Min(maxH - vm.StampHeight, _initialStampY + deltaY));

            vm.StampX = Math.Round(newX);
            vm.StampY = Math.Round(newY);
        }
    }

    private void Stamp_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_isDraggingStamp && sender is UIElement elem)
        {
            _isDraggingStamp = false;
            elem.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void ResizeHandle_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is BatchProcessingViewModel vm && sender is UIElement elem)
        {
            _isResizingStamp = true;
            _dragStartPoint = e.GetPosition(StampCanvas);
            _initialStampW = vm.StampWidth;
            _initialStampH = vm.StampHeight;
            elem.CaptureMouse();
            e.Handled = true;
        }
    }

    private void ResizeHandle_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isResizingStamp && DataContext is BatchProcessingViewModel vm)
        {
            var cur = e.GetPosition(StampCanvas);
            double deltaX = cur.X - _dragStartPoint.X;
            double deltaY = cur.Y - _dragStartPoint.Y;

            double maxW = StampCanvas.ActualWidth > 0 ? StampCanvas.ActualWidth : vm.CanvasRenderWidth;
            double maxH = StampCanvas.ActualHeight > 0 ? StampCanvas.ActualHeight : vm.CanvasRenderHeight;

            double newW = Math.Max(40, Math.Min(maxW - vm.StampX, _initialStampW + deltaX));
            double newH = Math.Max(20, Math.Min(maxH - vm.StampY, _initialStampH + deltaY));

            vm.StampWidth = Math.Round(newW);
            vm.StampHeight = Math.Round(newH);
        }
    }

    private void ResizeHandle_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_isResizingStamp && sender is UIElement elem)
        {
            _isResizingStamp = false;
            elem.ReleaseMouseCapture();
            e.Handled = true;
        }
    }
}
