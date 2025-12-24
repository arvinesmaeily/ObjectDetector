using CommunityToolkit.Maui.Views;
using Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using System.Diagnostics;

namespace ObjectDetector.Pages
{
    public partial class CameraPage : ContentPage
    {
        private YoloDetector? _detector;
        private readonly DetectionDrawable _drawable = new();
        private bool _isProcessing;
        private bool _showDetections = true;
        private float _confidenceThreshold = 0.25f;
        private float _iouThreshold = 0.45f;
        private DateTime _lastFrameTime = DateTime.UtcNow;
        private int _frameCount;
        private double _fps;
        private IReadOnlyList<Detection> _currentDetections = Array.Empty<Detection>();
        private CancellationTokenSource? _loopCts;
        private SKBitmap? _lastCapturedFrame;

        public CameraPage()
        {
            InitializeComponent();
            _detector = new YoloDetector();
            OverlayView.Drawable = _drawable;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (!await RequestCameraPermissionAsync())
            {
                Debug.WriteLine("Camera permission denied");
                return;
            }

            _loopCts = new CancellationTokenSource();
            _ = StartCaptureLoopAsync(_loopCts.Token);
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            
            _loopCts?.Cancel();
            _loopCts?.Dispose();
            _lastCapturedFrame?.Dispose();
        }

        private async Task<bool> RequestCameraPermissionAsync()
        {
            try
            {
                var status = await Permissions.CheckStatusAsync<Permissions.Camera>();

                if (status != PermissionStatus.Granted)
                {
                    status = await Permissions.RequestAsync<Permissions.Camera>();
                }

                return status == PermissionStatus.Granted;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Permission error: {ex}");
                return false;
            }
        }

        private void OnThresholdChanged(object sender, ValueChangedEventArgs e)
        {
            _confidenceThreshold = (float)e.NewValue;
            MainThread.BeginInvokeOnMainThread(() =>
                ThresholdValueLabel.Text = $"Threshold: {_confidenceThreshold:F2}");
        }

        private void OnIouChanged(object sender, ValueChangedEventArgs e)
        {
            _iouThreshold = (float)e.NewValue;
        }

        private void OnToggleDetections(object sender, EventArgs e)
        {
            _showDetections = !_showDetections;
            _drawable.SetShowDetections(_showDetections);
            ToggleDetectionsButton.Text = _showDetections ? "👁️ Hide Boxes" : "👁️ Show Boxes";
            OverlayView.Invalidate();
        }

        private async Task StartCaptureLoopAsync(CancellationToken token)
        {
            const int targetSize = 640;

            try
            {
                Debug.WriteLine("Camera loop started...");

                while (!token.IsCancellationRequested)
                {
                    if (_isProcessing)
                    {
                        await Task.Delay(5, token);
                        continue;
                    }

                    _isProcessing = true;

                    try
                    {
                        using var captureCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(captureCts.Token, token);

                        using (var imageStream = await Camera.CaptureImage(linkedCts.Token))
                        {
                            if (imageStream == null)
                            {
                                await Task.Delay(100, token);
                                continue;
                            }

                            var (inputData, modelW, modelH, origW, origH, scale, padX, padY) =
                                await ConvertAndPreprocessAsync(imageStream, targetSize, targetSize);

                            var detections = _detector!.Detect(inputData, modelW, modelH, _confidenceThreshold, _iouThreshold);
                            var detectionsOrig = MapDetectionsToOriginal(detections, scale, padX, padY);

                            _frameCount++;
                            var now = DateTime.UtcNow;
                            var elapsed = (now - _lastFrameTime).TotalSeconds;

                            if (elapsed >= 1.0)
                            {
                                _fps = _frameCount / elapsed;
                                _frameCount = 0;
                                _lastFrameTime = now;

                                MainThread.BeginInvokeOnMainThread(() =>
                                {
                                    FpsLabel.Text = $"FPS: {_fps:F1}";
                                    DetectionCountLabel.Text = $"Detections: {detectionsOrig.Count}";
                                });
                            }

                            _currentDetections = detectionsOrig;

                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                if (_showDetections)
                                {
                                    // Pass the OverlayView dimensions for proper scaling
                                    _drawable.UpdateDetections(detectionsOrig, origW, origH, OverlayView.Width, OverlayView.Height);
                                }
                                OverlayView.Invalidate();
                            });
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        // Timeout - continue
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Detection error: {ex}");
                    }
                    finally
                    {
                        _isProcessing = false;
                    }

                    await Task.Delay(100, token);
                }
            }
            catch (OperationCanceledException)
            {
                // Loop cancelled
            }
        }

        private static IReadOnlyList<Detection> MapDetectionsToOriginal(IReadOnlyList<Detection> detections, float scale, int padX, int padY)
        {
            if (detections == null || detections.Count == 0)
                return Array.Empty<Detection>();

            var result = new List<Detection>(detections.Count);

            foreach (var d in detections)
            {
                float x = (d.X - padX) / scale;
                float y = (d.Y - padY) / scale;
                float w = d.Width / scale;
                float h = d.Height / scale;

                result.Add(new Detection(
                    X: x,
                    Y: y,
                    Width: w,
                    Height: h,
                    Label: d.Label,
                    Confidence: d.Confidence));
            }

            return result;
        }

        private async Task<(float[] inputData, int modelWidth, int modelHeight, int origWidth, int origHeight, float scale, int padX, int padY)> 
            ConvertAndPreprocessAsync(Stream imageStream, int targetWidth, int targetHeight)
        {
            using var ms = new MemoryStream();
            await imageStream.CopyToAsync(ms);
            ms.Position = 0;

            using var bitmap = SKBitmap.Decode(ms);
            if (bitmap == null)
                throw new InvalidOperationException("Failed to decode image.");

            _lastCapturedFrame?.Dispose();
            _lastCapturedFrame = bitmap.Copy();

            int origWidth = bitmap.Width;
            int origHeight = bitmap.Height;

            float scale = Math.Min(
                targetWidth / (float)origWidth,
                targetHeight / (float)origHeight);

            int resizedWidth = (int)(origWidth * scale);
            int resizedHeight = (int)(origHeight * scale);

            using var resized = bitmap.Resize(
                new SKImageInfo(resizedWidth, resizedHeight),
                SKSamplingOptions.Default);

            if (resized == null)
                throw new InvalidOperationException("Failed to resize image.");

            int padX = (targetWidth - resizedWidth) / 2;
            int padY = (targetHeight - resizedHeight) / 2;

            using var letterboxed = new SKBitmap(targetWidth, targetHeight);
            using (var canvas = new SKCanvas(letterboxed))
            {
                canvas.Clear(SKColors.Black);
                canvas.DrawBitmap(resized, padX, padY);
            }

            var pixels = letterboxed.Pixels;
            var inputData = new float[targetWidth * targetHeight * 3];
            int hw = targetWidth * targetHeight;

            for (int y = 0; y < targetHeight; y++)
            {
                for (int x = 0; x < targetWidth; x++)
                {
                    int idx = y * targetWidth + x;
                    var color = pixels[idx];

                    inputData[idx] = color.Red / 255f;
                    inputData[idx + hw] = color.Green / 255f;
                    inputData[idx + 2 * hw] = color.Blue / 255f;
                }
            }

            return (inputData, targetWidth, targetHeight, origWidth, origHeight, scale, padX, padY);
        }

        private async void OnCaptureClicked(object sender, EventArgs e)
        {
            try
            {
                if (_lastCapturedFrame == null)
                {
                    await DisplayAlertAsync("No Frame", "No frame available to capture. Please wait for detection.", "OK");
                    return;
                }

                using var captured = new SKBitmap(_lastCapturedFrame.Width, _lastCapturedFrame.Height);
                using (var canvas = new SKCanvas(captured))
                {
                    canvas.DrawBitmap(_lastCapturedFrame, 0, 0);

                    var detections = _drawable.GetCurrentDetections();
                    if (detections.Count > 0)
                    {
                        DrawDetectionsOnCanvas(canvas, detections, _lastCapturedFrame.Width, _lastCapturedFrame.Height);
                    }
                }

                string saveDir = GetSaveDirectory();
                Directory.CreateDirectory(saveDir);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string filename = $"detection_{timestamp}.png";
                string filePath = Path.Combine(saveDir, filename);

                using (var image = SKImage.FromBitmap(captured))
                using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
                using (var stream = File.OpenWrite(filePath))
                {
                    data.SaveTo(stream);
                }

#if ANDROID
                NotifyAndroidMediaScanner(filePath);
#endif

                await DisplayAlertAsync("Saved", $"Photo saved to:\n{filename}", "OK");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Capture error: {ex}");
                await DisplayAlertAsync("Error", $"Failed to save photo:\n{ex.Message}", "OK");
            }
        }

        private void DrawDetectionsOnCanvas(SKCanvas canvas, IReadOnlyList<Detection> detections, int canvasWidth, int canvasHeight)
        {
            var paint = new SKPaint { StrokeWidth = 2, Color = SKColors.Lime, Style = SKPaintStyle.Stroke };
            var font = new SKFont(SKTypeface.Default, 16);
            var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            var bgPaint = new SKPaint { Color = SKColor.Parse("#00FF00"), Style = SKPaintStyle.Fill };

            foreach (var det in detections)
            {
                var rect = new SKRect(det.X, det.Y, det.X + det.Width, det.Y + det.Height);
                canvas.DrawRect(rect, paint);

                string label = $"{det.Label} {det.Confidence:P0}";
                font.MeasureText(label, out SKRect textBounds);

                float textX = det.X;
                float textY = det.Y - textBounds.Height - 8;

                canvas.DrawRect(textX, textY, textBounds.Width + 8, textBounds.Height + 4, bgPaint);
                canvas.DrawText(label, textX + 4, textY + textBounds.Height, SKTextAlign.Left, font, textPaint);
            }
        }

        private string GetSaveDirectory()
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
        private void NotifyAndroidMediaScanner(string filePath)
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

        private async void OnOpenFolderClicked(object sender, EventArgs e)
        {
            try
            {
                string saveDir = GetSaveDirectory();

#if WINDOWS
                Process.Start("explorer.exe", saveDir);
#elif ANDROID
                OpenAndroidFolder(saveDir);
#elif IOS || MACCATALYST
                await Launcher.OpenAsync(new OpenFileRequest
                {
                    File = new ReadOnlyFile(saveDir)
                });
#else
                await DisplayAlertAsync("Folder Location", $"Files saved to:\n{saveDir}", "OK");
#endif
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Open folder error: {ex}");
                string saveDir = GetSaveDirectory();
                await DisplayAlertAsync("Folder Location", $"Files saved to:\n{saveDir}", "OK");
            }
        }

#if ANDROID
        private void OpenAndroidFolder(string saveDir)
        {
            try
            {
                var intent = new Android.Content.Intent(Android.Content.Intent.ActionView);
                var uri = Android.Net.Uri.Parse(saveDir);
                intent.SetData(uri);
                Android.App.Application.Context.StartActivity(intent);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open folder: {ex.Message}");
            }
        }
#endif
    }

    public class DetectionDrawable : IDrawable
    {
        private IReadOnlyList<Detection> _detections = Array.Empty<Detection>();
        private bool _showDetections = true;
        private double _originalWidth = 640;
        private double _originalHeight = 640;
        private double _displayWidth = 640;
        private double _displayHeight = 640;
        private static readonly Dictionary<string, Color> _colorCache = new Dictionary<string, Color>();

        public void UpdateDetections(IReadOnlyList<Detection> detections, double originalWidth, double originalHeight, double displayWidth, double displayHeight)
        {
            _detections = detections;
            _originalWidth = originalWidth;
            _originalHeight = originalHeight;
            _displayWidth = displayWidth > 0 ? displayWidth : 640;
            _displayHeight = displayHeight > 0 ? displayHeight : 640;
        }

        public void SetShowDetections(bool show)
        {
            _showDetections = show;
        }

        public IReadOnlyList<Detection> GetCurrentDetections() => _detections;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (!_showDetections || _detections.Count == 0)
                return;

            // Calculate aspect-fit scaling to match CameraView display
            float scaleX = (float)(dirtyRect.Width / _originalWidth);
            float scaleY = (float)(dirtyRect.Height / _originalHeight);
            float scale = Math.Min(scaleX, scaleY);

            // Calculate letterbox offsets (if any)
            float offsetX = (dirtyRect.Width - (float)_originalWidth * scale) / 2f;
            float offsetY = (dirtyRect.Height - (float)_originalHeight * scale) / 2f;

            foreach (var detection in _detections)
            {
                var color = GetColorForLabel(detection.Label);

                // Scale and offset the detection coordinates
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