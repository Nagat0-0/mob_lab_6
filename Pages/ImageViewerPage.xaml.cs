namespace lab_6.Pages;

public partial class ImageViewerPage : ContentPage
{
    private double _currentScale = 1;
    private double _startScale = 1;
    private double _xOffset;
    private double _yOffset;

    public ImageViewerPage(ImageSource source, string title)
    {
        InitializeComponent();
        ZoomImage.Source = source;
        ViewerTitleLabel.Text = title;
    }

    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (e.Status == GestureStatus.Started)
        {
            _startScale = ZoomImage.Scale;
            ZoomImage.AnchorX = 0.5;
            ZoomImage.AnchorY = 0.5;
        }
        else if (e.Status == GestureStatus.Running)
        {
            _currentScale = Math.Clamp(_startScale * e.Scale, 1, 4);
            ZoomImage.Scale = _currentScale;

            if (_currentScale <= 1.01)
            {
                ResetPosition();
            }
        }
        else if (e.Status == GestureStatus.Completed)
        {
            _xOffset = ZoomImage.TranslationX;
            _yOffset = ZoomImage.TranslationY;
        }
    }

    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (_currentScale <= 1.01)
            return;

        if (e.StatusType == GestureStatus.Running)
        {
            double maxX = Math.Max(0, (ZoomImage.Width * (_currentScale - 1)) / 2);
            double maxY = Math.Max(0, (ZoomImage.Height * (_currentScale - 1)) / 2);

            ZoomImage.TranslationX = Math.Clamp(_xOffset + e.TotalX, -maxX, maxX);
            ZoomImage.TranslationY = Math.Clamp(_yOffset + e.TotalY, -maxY, maxY);
        }
        else if (e.StatusType == GestureStatus.Completed)
        {
            _xOffset = ZoomImage.TranslationX;
            _yOffset = ZoomImage.TranslationY;
        }
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_currentScale > 1.05)
        {
            _currentScale = 1;
            ZoomImage.Scale = 1;
            ResetPosition();
        }
        else
        {
            _currentScale = 2.5;
            ZoomImage.AnchorX = 0.5;
            ZoomImage.AnchorY = 0.5;
            ZoomImage.Scale = _currentScale;
            ResetPosition();
        }
    }

    private void ResetPosition()
    {
        _xOffset = 0;
        _yOffset = 0;
        ZoomImage.TranslationX = 0;
        ZoomImage.TranslationY = 0;
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}