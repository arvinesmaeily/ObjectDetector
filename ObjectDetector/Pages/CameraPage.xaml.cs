using CommunityToolkit.Maui.Views;
using ObjectDetector.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using System.Diagnostics;

namespace ObjectDetector.Pages
{
    public partial class CameraPage : ContentPage
    {
        private readonly YoloDetector? _detector;
        private readonly DetectionDrawable _drawable = new();
        private bool _isProcessing;
        private bool _showDetections = true;
        private float _confidenceThreshold = 0.25f;
        private float _iouThreshold = 0.45f;
        private DateTime _lastFrameTime = DateTime.UtcNow;
        private int _frameCount;
        private double _fps;
        private IReadOnlyList<Detection> _currentDetections = [];
        private CancellationTokenSource? _loopCts;
        private SKBitmap? _lastCapturedFrame;

        private const string ConfidenceThresholdKey = "ConfidenceThreshold";
        private const string IouThresholdKey = "IouThreshold";

        public CameraPage()
        {
            InitializeComponent();
            _detector = new YoloDetector();
            OverlayView.Drawable = _drawable;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // Load thresholds from settings
            _confidenceThreshold = Preferences.Get(ConfidenceThresholdKey, 0.25f);
            _iouThreshold = Preferences.Get(IouThresholdKey, 0.45f);

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

        private static async Task<bool> RequestCameraPermissionAsync()
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
                        // Reload thresholds each iteration to pick up changes from Settings
                        _confidenceThreshold = Preferences.Get(ConfidenceThresholdKey, 0.25f);
                        _iouThreshold = Preferences.Get(IouThresholdKey, 0.45f);

                        using var captureCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(captureCts.Token, token);

                        using var imageStream = await Camera.CaptureImage(linkedCts.Token);
                        if (imageStream == null)
                        {
                            await Task.Delay(100, token);
                            continue;
                        }

                        var (inputData, modelW, modelH, origW, origH, scale, padX, padY, displayW, displayH) =
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
                                _drawable.UpdateDetections(detectionsOrig, displayW, displayH);
                            }
                            OverlayView.Invalidate();
                        });
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

        private static List<Detection> MapDetectionsToOriginal(List<Detection> detections, float scale, int padX, int padY)
        {
            if (detections == null || detections.Count == 0)
                return [];

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

        private async Task<(float[] inputData, int modelWidth, int modelHeight, int origWidth, int origHeight, float scale, int padX, int padY, int displayWidth, int displayHeight)>
            ConvertAndPreprocessAsync(Stream imageStream, int targetWidth, int targetHeight)
        {
            using var ms = new MemoryStream();
            await imageStream.CopyToAsync(ms);
            ms.Position = 0;

            using var bitmap = SKBitmap.Decode(ms) ?? throw new InvalidOperationException("Failed to decode image.");
            int origWidth = bitmap.Width;
            int origHeight = bitmap.Height;

            Debug.WriteLine($"[Camera] Raw bitmap size: {origWidth}x{origHeight}");

            // On Android, camera often captures in landscape but display is portrait
            // Check if we need to rotate based on aspect ratio mismatch with display
            SKBitmap workingBitmap = bitmap;
            int displayWidth = origWidth;
            int displayHeight = origHeight;

#if ANDROID
            var displayInfo = DeviceDisplay.MainDisplayInfo;
            bool displayIsPortrait = displayInfo.Height > displayInfo.Width;
            bool bitmapIsLandscape = origWidth > origHeight;

            Debug.WriteLine($"[Camera] Display portrait: {displayIsPortrait}, Bitmap landscape: {bitmapIsLandscape}");

            // If display is portrait but bitmap is landscape, rotate 90° clockwise
            if (displayIsPortrait && bitmapIsLandscape)
            {
                Debug.WriteLine("[Camera] Rotating bitmap 90° CW to match display orientation");
                var rotated = new SKBitmap(origHeight, origWidth);
                using (var canvas = new SKCanvas(rotated))
                {
                    canvas.Translate(origHeight, 0);
                    canvas.RotateDegrees(90);
                    canvas.DrawBitmap(bitmap, 0, 0);
                }
                workingBitmap = rotated;
                displayWidth = origHeight;
                displayHeight = origWidth;
                Debug.WriteLine($"[Camera] After rotation: {displayWidth}x{displayHeight}");
            }
#endif

            _lastCapturedFrame?.Dispose();
            _lastCapturedFrame = workingBitmap.Copy();

            float scale = Math.Min(
                targetWidth / (float)displayWidth,
                targetHeight / (float)displayHeight);

            int resizedWidth = (int)(displayWidth * scale);
            int resizedHeight = (int)(displayHeight * scale);

            using var resized = workingBitmap.Resize(
                new SKImageInfo(resizedWidth, resizedHeight),
                SKSamplingOptions.Default) ?? throw new InvalidOperationException("Failed to resize image.");
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

            // Clean up rotated bitmap if we created one
#if ANDROID
            if (workingBitmap != bitmap)
            {
                workingBitmap.Dispose();
            }
#endif

            return (inputData, targetWidth, targetHeight, origWidth, origHeight, scale, padX, padY, displayWidth, displayHeight);
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
    }
}