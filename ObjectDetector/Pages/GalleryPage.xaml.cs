using SkiaSharp;
using System.Diagnostics;
using ObjectDetector.Services;

namespace ObjectDetector.Pages
{
    public partial class GalleryPage : ContentPage
    {
        private readonly YoloDetector? _detector;
        private readonly GalleryDetectionDrawable _drawable = new();
        private List<Detection> _currentDetections = [];
        private double _imageWidth = 640;
        private double _imageHeight = 640;
        private SKBitmap? _currentBitmap;

        private const string ConfidenceThresholdKey = "ConfidenceThreshold";
        private const string IouThresholdKey = "IouThreshold";

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
                SaveButton.IsVisible = false;

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

                _currentBitmap?.Dispose();
                _currentBitmap = SKBitmap.Decode(ms);
                
                if (_currentBitmap == null)
                {
                    ResultLabel.Text = "Failed to decode image";
                    return;
                }

                _imageWidth = _currentBitmap.Width;
                _imageHeight = _currentBitmap.Height;

                int inputWidth = 640;
                int inputHeight = 640;

                using var resized = _currentBitmap.Resize(
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

                // Load thresholds from settings
                var confidenceThreshold = Preferences.Get(ConfidenceThresholdKey, 0.25f);
                var iouThreshold = Preferences.Get(IouThresholdKey, 0.45f);

                var detections = await Task.Run(() =>
                    _detector!.Detect(imageData, inputWidth, inputHeight, confidenceThreshold, iouThreshold));

                // Map detections back to original image size
                var mappedDetections = MapDetectionsToOriginal(detections, _currentBitmap.Width, _currentBitmap.Height, inputWidth, inputHeight);

                _currentDetections = mappedDetections;
                ResultLabel.Text = $"Found {detections.Count} objects";
                SaveButton.IsVisible = detections.Count > 0;

                _drawable.UpdateDetections(mappedDetections, _imageWidth, _imageHeight);
                MainThread.BeginInvokeOnMainThread(() => OverlayView.Invalidate());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing image: {ex}");
                ResultLabel.Text = $"Error: {ex.Message}";
            }
        }

        private async void OnSaveImageClicked(object sender, EventArgs e)
        {
            try
            {
                if (_currentBitmap == null || _currentDetections.Count == 0)
                {
                    await DisplayAlertAsync("No Data", "No image or detections available to save.", "OK");
                    return;
                }

                using var captured = new SKBitmap(_currentBitmap.Width, _currentBitmap.Height);
                using (var canvas = new SKCanvas(captured))
                {
                    canvas.DrawBitmap(_currentBitmap, 0, 0);
                    DrawDetectionsOnCanvas(canvas, _currentDetections, _currentBitmap.Width, _currentBitmap.Height);
                }

                string saveDir = GetSaveDirectory();
                Directory.CreateDirectory(saveDir);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string filename = $"gallery_detection_{timestamp}.png";
                string filePath = Path.Combine(saveDir, filename);

                using (var image = SKImage.FromBitmap(captured))
                using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
                using (var fileStream = File.OpenWrite(filePath))
                {
                    data.SaveTo(fileStream);
                }

#if ANDROID
                NotifyAndroidMediaScanner(filePath);
#endif

                await DisplayAlertAsync("Saved", $"Image saved to:\n{filename}", "OK");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Save error: {ex}");
                await DisplayAlertAsync("Error", $"Failed to save image:\n{ex.Message}", "OK");
            }
        }

        private static SKColor GetColorForLabel(string label)
        {
            var hash = label.GetHashCode();
            var hue = (hash & 0xFF) / 255f;
            var saturation = 0.7f + ((hash >> 8) & 0x1F) / 255f;
            var brightness = 0.8f + ((hash >> 16) & 0x1F) / 255f;
            
            return SKColor.FromHsv(hue * 360f, saturation * 100f, brightness * 100f);
        }

        private static void DrawDetectionsOnCanvas(SKCanvas canvas, IReadOnlyList<Detection> detections, int canvasWidth, int canvasHeight)
        {
            var (fontSize, strokeWidth, paddingX, paddingY) = DetectionRenderingConfig.GetScaledValues(canvasWidth, canvasHeight);

            var font = new SKFont(SKTypeface.Default, fontSize);
            var textPaint = new SKPaint { IsAntialias = true, Color = SKColors.White };

            foreach (var det in detections)
            {
                var color = GetColorForLabel(det.Label);
                var paint = new SKPaint { StrokeWidth = strokeWidth, Color = color, Style = SKPaintStyle.Stroke };
                
                var rect = new SKRect(det.X, det.Y, det.X + det.Width, det.Y + det.Height);
                canvas.DrawRect(rect, paint);

                string label = $"{det.Label} {det.Confidence:P0}";
                font.MeasureText(label, out SKRect textBounds);

                float bgWidth = textBounds.Width + paddingX * 2;
                float bgHeight = textBounds.Height + paddingY * 2;

                float textX = det.X;
                float textY = det.Y - bgHeight;
                
                // If label would go off top, put it inside the box at the top
                if (textY < 0)
                    textY = det.Y + paddingY;
                
                // If label would go off left edge
                if (textX < 0)
                    textX = 0;
                
                // If label would go off right edge
                if (textX + bgWidth > canvasWidth)
                    textX = canvasWidth - bgWidth;
                
                // If label would go off bottom edge (when placed inside box)
                if (textY + bgHeight > canvasHeight)
                    textY = canvasHeight - bgHeight;

                var bgPaint = new SKPaint { Color = color, Style = SKPaintStyle.Fill };
                canvas.DrawRect(textX, textY, bgWidth, bgHeight, bgPaint);

                canvas.DrawText(label, textX + paddingX, textY + textBounds.Height + paddingY, SKTextAlign.Left, font, textPaint);
            }
        }

        private static string GetSaveDirectory()
        {
#if ANDROID
            return Path.Combine(Android.OS.Environment.GetExternalStoragePublicDirectory(
                Android.OS.Environment.DirectoryPictures)!.AbsolutePath, "ObjectDetector");
#elif WINDOWS
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ObjectDetector");
#elif IOS || MACCATALYST
            return Path.Combine(FileSystem.AppDataDirectory, "Captures");
#else
            return FileSystem.AppDataDirectory;
#endif
        }

#if ANDROID
        private static void NotifyAndroidMediaScanner(string filePath)
        {
            try
            {
                var intent = new Android.Content.Intent(Android.Content.Intent.ActionMediaScannerScanFile);
                var file = new Java.IO.File(filePath);
                var uri = Android.Net.Uri.FromFile(file);
                intent.SetData(uri);
                Android.App.Application.Context.SendBroadcast(intent);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to notify media scanner: {ex.Message}");
            }
        }
#endif

        private static List<Detection> MapDetectionsToOriginal(List<Detection> detections, int originalWidth, int originalHeight, int modelWidth, int modelHeight)
        {
            if (detections == null || detections.Count == 0)
                return [];

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
        private IReadOnlyList<Detection> _detections = [];
        private double _imageWidth = 640;
        private double _imageHeight = 640;
        private static readonly Dictionary<string, Color> _colorCache = [];

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

            if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0 || _imageWidth <= 0 || _imageHeight <= 0)
                return;

            // Calculate aspect-fit scaling to match Image display
            float scaleX = dirtyRect.Width / (float)_imageWidth;
            float scaleY = dirtyRect.Height / (float)_imageHeight;
            float scale = Math.Min(scaleX, scaleY);

            // Calculate letterbox offsets
            float offsetX = (dirtyRect.Width - (float)_imageWidth * scale) / 2f;
            float offsetY = (dirtyRect.Height - (float)_imageHeight * scale) / 2f;

            float drawWidth = (float)_imageWidth * scale;
            float drawHeight = (float)_imageHeight * scale;

            if (drawWidth <= 0 || drawHeight <= 0)
                return;

            var (fontSize, strokeWidth, paddingX, paddingY) = DetectionRenderingConfig.GetScaledValues(drawWidth, drawHeight);

            var font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.Font = font;
            canvas.FontSize = fontSize;

            foreach (var detection in _detections)
            {
                var color = GetColorForLabel(detection.Label);

                float x = detection.X * scale + offsetX;
                float y = detection.Y * scale + offsetY;
                float w = detection.Width * scale;
                float h = detection.Height * scale;

                // Draw bounding box
                canvas.StrokeColor = color;
                canvas.StrokeSize = strokeWidth;
                canvas.DrawRectangle(x, y, w, h);

                // Draw label
                string text = $"{detection.Label} {detection.Confidence:P0}";
                var textSize = canvas.GetStringSize(text, font, fontSize);

                float bgWidth = textSize.Width + paddingX * 2;
                float bgHeight = textSize.Height + paddingY * 2;

                float bgX = x;
                float bgY = y - bgHeight;

                // If label would go off top, put it inside the box
                if (bgY < offsetY)
                    bgY = y + paddingY;

                // Draw label background
                canvas.FillColor = color;
                canvas.FillRectangle(bgX, bgY, bgWidth, bgHeight);

                // Draw label text
                canvas.FontColor = Colors.White;
                canvas.DrawString(text, bgX + paddingX, bgY + paddingY, bgWidth, bgHeight, HorizontalAlignment.Left, VerticalAlignment.Top);
            }
        }

        private static Color GetColorForLabel(string label)
        {
            if (_colorCache.TryGetValue(label, out var cachedColor))
                return cachedColor;

            // Generate a vibrant color based on label hash
            var hash = label.GetHashCode();
            var hue = (hash & 0xFF) / 255f;
            var saturation = 0.7f + ((hash >> 8) & 0x1F) / 255f;
            var brightness = 0.8f + ((hash >> 16) & 0x1F) / 255f;

            // Convert HSB to RGB
            var color = ColorFromHsv(hue * 360f, saturation, brightness);
            _colorCache[label] = color;
            return color;
        }

        private static Color ColorFromHsv(float hue, float saturation, float value)
        {
            int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
            float f = hue / 60 - (float)Math.Floor(hue / 60);

            float v = value;
            float p = value * (1 - saturation);
            float q = value * (1 - f * saturation);
            float t = value * (1 - (1 - f) * saturation);

            if (hi == 0)
                return Color.FromRgba(v, t, p, 1f);
            else if (hi == 1)
                return Color.FromRgba(q, v, p, 1f);
            else if (hi == 2)
                return Color.FromRgba(p, v, t, 1f);
            else if (hi == 3)
                return Color.FromRgba(p, q, v, 1f);
            else if (hi == 4)
                return Color.FromRgba(t, p, v, 1f);
            else
                return Color.FromRgba(v, p, q, 1f);
        }
    }
}