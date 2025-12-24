using Services;
using SkiaSharp;
using System.Diagnostics;

namespace ObjectDetector.Pages
{
    public partial class GalleryPage : ContentPage
    {
        private YoloDetector? _detector;
        private readonly GalleryDetectionDrawable _drawable = new();
        private IReadOnlyList<Detection> _currentDetections = Array.Empty<Detection>();
        private double _imageWidth = 640;
        private double _imageHeight = 640;

        public GalleryPage()
        {
            InitializeComponent();
            _detector = new YoloDetector();
            OverlayView.Drawable = _drawable;
        }

        private async void OnPickImageClicked(object sender, EventArgs e)
        {
            try
            {
                var result = await FilePicker.PickAsync(new PickOptions
                {
                    PickerTitle = "Select an image",
                    FileTypes = FilePickerFileType.Images
                });

                if (result == null)
                    return;

                ResultLabel.Text = "Processing...";

                using var stream = await result.OpenReadAsync();
                SelectedImage.Source = ImageSource.FromStream(() => result.OpenReadAsync().Result);

                await ProcessImageAsync(stream);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error picking image: {ex}");
                await DisplayAlertAsync("Error", $"Failed to process image:\n{ex.Message}", "OK");
            }
        }

        private async Task ProcessImageAsync(Stream stream)
        {
            try
            {
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                ms.Position = 0;

                using var skImage = SKBitmap.Decode(ms);
                if (skImage == null)
                {
                    ResultLabel.Text = "Failed to decode image";
                    return;
                }

                _imageWidth = skImage.Width;
                _imageHeight = skImage.Height;

                int inputWidth = 640;
                int inputHeight = 640;

                using var resized = skImage.Resize(
                    new SKImageInfo(inputWidth, inputHeight),
                    SKSamplingOptions.Default);

                if (resized == null)
                {
                    ResultLabel.Text = "Failed to resize image";
                    return;
                }

                float[] imageData = new float[3 * inputHeight * inputWidth];
                int idx = 0;

                for (int c = 0; c < 3; c++)
                {
                    for (int y = 0; y < inputHeight; y++)
                    {
                        for (int x = 0; x < inputWidth; x++)
                        {
                            var pixel = resized.GetPixel(x, y);
                            byte val = c switch
                            {
                                0 => pixel.Red,
                                1 => pixel.Green,
                                _ => pixel.Blue
                            };
                            imageData[idx++] = val / 255f;
                        }
                    }
                }

                var detections = await Task.Run(() =>
                    _detector!.Detect(imageData, inputWidth, inputHeight, 0.25f, 0.45f));

                // Map detections back to original image size
                var mappedDetections = MapDetectionsToOriginal(detections, skImage.Width, skImage.Height, inputWidth, inputHeight);

                _currentDetections = mappedDetections;
                ResultLabel.Text = $"Found {detections.Count} objects";

                _drawable.UpdateDetections(mappedDetections, _imageWidth, _imageHeight);
                MainThread.BeginInvokeOnMainThread(() => OverlayView.Invalidate());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing image: {ex}");
                ResultLabel.Text = $"Error: {ex.Message}";
            }
        }

        private static IReadOnlyList<Detection> MapDetectionsToOriginal(IReadOnlyList<Detection> detections, int originalWidth, int originalHeight, int modelWidth, int modelHeight)
        {
            if (detections == null || detections.Count == 0)
                return Array.Empty<Detection>();

            float scaleX = (float)originalWidth / modelWidth;
            float scaleY = (float)originalHeight / modelHeight;

            var result = new List<Detection>(detections.Count);

            foreach (var d in detections)
            {
                result.Add(new Detection(
                    X: d.X * scaleX,
                    Y: d.Y * scaleY,
                    Width: d.Width * scaleX,
                    Height: d.Height * scaleY,
                    Label: d.Label,
                    Confidence: d.Confidence));
            }

            return result;
        }
    }

    public class GalleryDetectionDrawable : IDrawable
    {
        private IReadOnlyList<Detection> _detections = Array.Empty<Detection>();
        private double _imageWidth = 640;
        private double _imageHeight = 640;
        private static readonly Dictionary<string, Color> _colorCache = new Dictionary<string, Color>();

        public void UpdateDetections(IReadOnlyList<Detection> detections, double imageWidth, double imageHeight)
        {
            _detections = detections;
            _imageWidth = imageWidth;
            _imageHeight = imageHeight;
        }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (_detections.Count == 0)
                return;

            // Calculate aspect-fit scaling to match Image display
            float scaleX = dirtyRect.Width / (float)_imageWidth;
            float scaleY = dirtyRect.Height / (float)_imageHeight;
            float scale = Math.Min(scaleX, scaleY);

            // Calculate letterbox offsets
            float offsetX = (dirtyRect.Width - (float)_imageWidth * scale) / 2f;
            float offsetY = (dirtyRect.Height - (float)_imageHeight * scale) / 2f;

            foreach (var detection in _detections)
            {
                var color = GetColorForLabel(detection.Label);

                float x = detection.X * scale + offsetX;
                float y = detection.Y * scale + offsetY;
                float w = detection.Width * scale;
                float h = detection.Height * scale;

                // Draw bounding box
                canvas.StrokeColor = color;
                canvas.StrokeSize = 3;
                canvas.DrawRectangle(x, y, w, h);

                // Draw label background
                canvas.FillColor = color.WithAlpha(0.7f);
                float labelHeight = 20;
                float labelWidth = 150;
                canvas.FillRectangle(x, y - labelHeight, labelWidth, labelHeight);

                // Draw label text
                canvas.FontColor = Colors.White;
                canvas.FontSize = 14;
                canvas.DrawString($"{detection.Label} {detection.Confidence:P0}", x + 2, y - 5, HorizontalAlignment.Left);
            }
        }

        private static Color GetColorForLabel(string label)
        {
            if (_colorCache.TryGetValue(label, out var color))
                return color;

            var hash = label.GetHashCode();
            var r = (hash & 0xFF) / 255f;
            var g = ((hash >> 8) & 0xFF) / 255f;
            var b = ((hash >> 16) & 0xFF) / 255f;

            color = new Color(r, g, b);
            _colorCache[label] = color;
            return color;
        }
    }
}