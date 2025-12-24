using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls.PlatformConfiguration;
using Services;
using SkiaSharp;
using System.Diagnostics;

namespace ObjectDetector
{
    public partial class MainPage : ContentPage
    {
        private readonly YoloDetector _detector;
        private readonly DetectionDrawable _drawable = new();

        private bool _isProcessing;
        private CancellationTokenSource? _loopCts;
        
        private float _currentConfidenceThreshold = 0.25f;
        private float _currentIouThreshold = 0.45f;
        private bool _showDetections = true;
        
        private int _frameCount = 0;
        private Stopwatch _fpsStopwatch = Stopwatch.StartNew();
        private SKBitmap? _lastCapturedFrame;

        public MainPage()
        {
            InitializeComponent();

            _detector = new YoloDetector();
            OverlayView.Drawable = _drawable;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // Keep screen on while app is active
            DeviceDisplay.Current.KeepScreenOn = true;

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
            
            // Allow screen to lock again when app is not active
            DeviceDisplay.Current.KeepScreenOn = false;
            
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

        private string GetSaveDirectory()
        {
            string saveDir = "";
            
            if (DeviceInfo.Platform == DevicePlatform.Android)
            {
                saveDir = GetAndroidPicturesPath();
            }
            else if (DeviceInfo.Platform == DevicePlatform.WinUI)
            {
                var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                saveDir = Path.Combine(documentsPath, "ObjectDetector");
            }
            else if (DeviceInfo.Platform == DevicePlatform.iOS || DeviceInfo.Platform == DevicePlatform.MacCatalyst)
            {
                var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                saveDir = Path.Combine(documentsPath, "ObjectDetector");
            }
            else
            {
                saveDir = Path.Combine(FileSystem.AppDataDirectory, "Captures");
            }

            if (!Directory.Exists(saveDir))
            {
                Directory.CreateDirectory(saveDir);
            }

            return saveDir;
        }

        private string GetAndroidPicturesPath()
        {
            // Use reflection to avoid compile-time dependency on Android APIs
            try
            {
                var envType = Type.GetType("Android.OS.Environment, Mono.Android");
                if (envType != null)
                {
                    var method = envType.GetMethod("GetExternalStoragePublicDirectory");
                    var dirPicturesField = envType.GetField("DirectoryPictures");
                    
                    if (method != null && dirPicturesField != null)
                    {
                        var dirPictures = dirPicturesField.GetValue(null) as string;
                        var result = method.Invoke(null, new object?[] { dirPictures });
                        var absPathProp = result?.GetType().GetProperty("AbsolutePath");
                        var picturesPath = absPathProp?.GetValue(result) as string;
                        
                        if (!string.IsNullOrEmpty(picturesPath))
                        {
                            return Path.Combine(picturesPath, "ObjectDetector");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to get Android pictures path: {ex.Message}");
            }

            // Fallback
            return Path.Combine(FileSystem.AppDataDirectory, "Captures");
        }

        private void OnThresholdChanged(object sender, ValueChangedEventArgs e)
        {
            _currentConfidenceThreshold = (float)e.NewValue;
            ThresholdValueLabel.Text = $"Threshold: {_currentConfidenceThreshold:F2}";
        }

        private void OnIouChanged(object sender, ValueChangedEventArgs e)
        {
            _currentIouThreshold = (float)e.NewValue;
        }

        private void OnToggleDetections(object sender, EventArgs e)
        {
            _showDetections = !_showDetections;
            _drawable.SetShowDetections(_showDetections);
            ToggleDetectionsButton.Text = _showDetections ? "👁️ Hide Boxes" : "👁️ Show Boxes";
            OverlayView.Invalidate();
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
                string fileName = $"detection_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                string filePath = Path.Combine(saveDir, fileName);

                using (var image = SKImage.FromBitmap(captured))
                using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
                using (var stream = File.OpenWrite(filePath))
                {
                    data.SaveTo(stream);
                }

                // Notify Android media scanner
                if (DeviceInfo.Platform == DevicePlatform.Android)
                {
                    NotifyAndroidMediaScanner(filePath);
                }

                await DisplayAlertAsync("Success", $"Saved to:\n{saveDir}\n\nFile: {fileName}", "OK");
                Debug.WriteLine($"Saved capture to: {filePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Capture error: {ex}");
                await DisplayAlertAsync("Error", $"Failed to capture: {ex.Message}", "OK");
            }
        }

        private void NotifyAndroidMediaScanner(string filePath)
        {
            try
            {
                var intentType = Type.GetType("Android.Content.Intent, Mono.Android");
                var uriType = Type.GetType("Android.Net.Uri, Mono.Android");
                var fileType = Type.GetType("Java.IO.File, Mono.Android");
                var appType = Type.GetType("Android.App.Application, Mono.Android");

                if (intentType != null && uriType != null && fileType != null && appType != null)
                {
                    var intent = Activator.CreateInstance(intentType, "android.intent.action.MEDIA_SCANNER_SCAN_FILE");
                    var file = Activator.CreateInstance(fileType, filePath);
                    var fromFileMethod = uriType.GetMethod("FromFile");
                    var uri = fromFileMethod?.Invoke(null, new[] { file });
                    
                    var setDataMethod = intentType.GetMethod("SetData");
                    setDataMethod?.Invoke(intent, new[] { uri });
                    
                    var contextProp = appType.GetProperty("Context");
                    var context = contextProp?.GetValue(null);
                    var sendBroadcastMethod = context?.GetType().GetMethod("SendBroadcast");
                    sendBroadcastMethod?.Invoke(context, new[] { intent });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to notify media scanner: {ex.Message}");
            }
        }

        private async void OnOpenFolderClicked(object sender, EventArgs e)
        {
            try
            {
                string saveDir = GetSaveDirectory();

                if (DeviceInfo.Platform == DevicePlatform.WinUI)
                {
                    Process.Start("explorer.exe", saveDir);
                }
                else if (DeviceInfo.Platform == DevicePlatform.Android)
                {
                    OpenAndroidFolder(saveDir);
                }
                else if (DeviceInfo.Platform == DevicePlatform.iOS || DeviceInfo.Platform == DevicePlatform.MacCatalyst)
                {
                    await Launcher.OpenAsync(new OpenFileRequest
                    {
                        File = new ReadOnlyFile(saveDir)
                    });
                }
                else
                {
                    await DisplayAlertAsync("Folder Location", $"Files saved to:\n{saveDir}", "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Open folder error: {ex}");
                string saveDir = GetSaveDirectory();
                await DisplayAlertAsync("Folder Location", $"Files saved to:\n{saveDir}", "OK");
            }
        }

        private void OpenAndroidFolder(string saveDir)
        {
            try
            {
                var intentType = Type.GetType("Android.Content.Intent, Mono.Android");
                var uriType = Type.GetType("Android.Net.Uri, Mono.Android");
                var appType = Type.GetType("Android.App.Application, Mono.Android");

                if (intentType != null && uriType != null && appType != null)
                {
                    var intent = Activator.CreateInstance(intentType, "android.intent.action.VIEW");
                    var parseMethod = uriType.GetMethod("Parse", new[] { typeof(string) });
                    var uri = parseMethod?.Invoke(null, new object[] { saveDir });
                    
                    var setDataAndTypeMethod = intentType.GetMethod("SetDataAndType", new[] { uriType, typeof(string) });
                    setDataAndTypeMethod?.Invoke(intent, new[] { uri, "resource/folder" });
                    
                    var addFlagsMethod = intentType.GetMethod("AddFlags");
                    var activityFlagsType = Type.GetType("Android.Content.ActivityFlags, Mono.Android");
                    var newTaskFlag = Enum.Parse(activityFlagsType, "NewTask");
                    addFlagsMethod?.Invoke(intent, new[] { newTaskFlag });
                    
                    var contextProp = appType.GetProperty("Context");
                    var context = contextProp?.GetValue(null);
                    var startActivityMethod = context?.GetType().GetMethod("StartActivity");
                    startActivityMethod?.Invoke(context, new[] { intent });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open Android folder: {ex.Message}");
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
                // Draw bounding box
                var rect = new SKRect(det.X, det.Y, det.X + det.Width, det.Y + det.Height);
                canvas.DrawRect(rect, paint);

                // Draw label with background
                string label = $"{det.Label} {det.Confidence:P0}";
                font.MeasureText(label, out SKRect textBounds);

                float textX = det.X;
                float textY = det.Y - textBounds.Height - 8;

                // Draw background rectangle
                canvas.DrawRect(textX, textY, textBounds.Width + 8, textBounds.Height + 4, bgPaint);

                // Draw text
                canvas.DrawText(label, textX + 4, textY + textBounds.Height, SKTextAlign.Left, font, textPaint);
            }
        }

        private async Task StartCaptureLoopAsync(CancellationToken token)
        {
            const int targetSize = 640; // model input size

            try
            {
                Debug.WriteLine("Running...");

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
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                            captureCts.Token, token);

                        using (var imageStream = await Camera.CaptureImage(linkedCts.Token))
                        {
                            if (imageStream == null)
                            {
                                await Task.Delay(100, token);
                                continue;
                            }

                            var (inputData,
                                 modelW, modelH,
                                 origW, origH,
                                 scale, padX, padY) =
                                await ConvertAndPreprocessAsync(imageStream, targetSize, targetSize);

                            // Run detection with current thresholds
                            var detectionsModel = _detector.Detect(inputData, modelW, modelH, _currentConfidenceThreshold, _currentIouThreshold);

                            // Map from model (letterboxed 640x640) -> original camera resolution
                            var detectionsOrig = MapDetectionsToOriginal(detectionsModel, scale, padX, padY);

                            // Update FPS counter
                            _frameCount++;
                            if (_fpsStopwatch.ElapsedMilliseconds >= 1000)
                            {
                                int fps = _frameCount;
                                _frameCount = 0;
                                _fpsStopwatch.Restart();

                                MainThread.BeginInvokeOnMainThread(() =>
                                {
                                    FpsLabel.Text = $"FPS: {fps}";
                                    DetectionCountLabel.Text = $"Detections: {detectionsOrig.Count}";
                                });
                            }

                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                if (_showDetections)
                                {
                                    _drawable.UpdateDetections(detectionsOrig, origW, origH);
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
                        MainThread.BeginInvokeOnMainThread(() =>
                            Debug.WriteLine($"Error: {ex.Message}"));
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
            finally
            {
                MainThread.BeginInvokeOnMainThread(() =>
                    Debug.WriteLine("Stopped"));
            }
        }

        private static IReadOnlyList<Detection> MapDetectionsToOriginal(IReadOnlyList<Detection> detections, float scale, int padX, int padY)
        {
            if (detections == null || detections.Count == 0)
                return Array.Empty<Detection>();

            var result = new List<Detection>(detections.Count);

            foreach (var d in detections)
            {
                // Model coords (letterboxed 640x640) -> original image coords
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
        
        private async Task<(float[] inputData, int modelWidth, int modelHeight, int origWidth, int origHeight, float scale, int padX, int padY)> ConvertAndPreprocessAsync(Stream imageStream, int targetWidth, int targetHeight)
        {
            using var ms = new MemoryStream();
            await imageStream.CopyToAsync(ms);
            ms.Position = 0;

            using var bitmap = SKBitmap.Decode(ms);
            if (bitmap == null)
                throw new InvalidOperationException("Failed to decode image.");

            // Store the last captured frame for screenshot capability
            _lastCapturedFrame?.Dispose();
            _lastCapturedFrame = bitmap.Copy();

            SKBitmap workingBitmap = bitmap;
            int origWidth = bitmap.Width;
            int origHeight = bitmap.Height;

            Debug.WriteLine($"[Camera] Original decoded image: {origWidth}x{origHeight}");

            // --- Determine rotation based on screen orientation ---
            if (DeviceInfo.Platform == DevicePlatform.Android)
            {
                var screenRotation = DeviceDisplay.MainDisplayInfo.Rotation;
                Debug.WriteLine($"[Android] Screen rotation: {screenRotation}");

                // Apply rotation only if bitmap is in landscape but screen is in portrait (or vice versa)
                if ((screenRotation == DisplayRotation.Rotation0 || screenRotation == DisplayRotation.Rotation180) && origWidth > origHeight)
                {
                    Debug.WriteLine($"[Android] Applying 90° rotation: {origWidth}x{origHeight} -> {origHeight}x{origWidth}");

                    // Screen is portrait, but camera frame is landscape - rotate 90° clockwise
                    var rotated = new SKBitmap(origHeight, origWidth);
                    using (var canvas = new SKCanvas(rotated))
                    {
                        canvas.Translate(origHeight, 0);
                        canvas.RotateDegrees(90);
                        canvas.DrawBitmap(workingBitmap, 0, 0);
                    }
                    workingBitmap = rotated;
                    int temp = origWidth;
                    origWidth = origHeight;
                    origHeight = temp;

                    Debug.WriteLine($"[Android] After rotation: {origWidth}x{origHeight}");
                }
            }

            // --- LETTERBOX to targetWidth x targetHeight ---
            float scale = Math.Min(
                targetWidth / (float)origWidth,
                targetHeight / (float)origHeight);

            Debug.WriteLine($"[Letterbox] Scale factor: {scale}");

            int resizedWidth = (int)(origWidth * scale);
            int resizedHeight = (int)(origHeight * scale);

            Debug.WriteLine($"[Letterbox] Resized to: {resizedWidth}x{resizedHeight}");

            using var resized = workingBitmap.Resize(
                new SKImageInfo(resizedWidth, resizedHeight),
                SKSamplingOptions.Default);

            if (resized == null)
                throw new InvalidOperationException("Failed to resize image.");

            int padX = (targetWidth - resizedWidth) / 2;
            int padY = (targetHeight - resizedHeight) / 2;

            Debug.WriteLine($"[Letterbox] Padding: padX={padX}, padY={padY}");
            Debug.WriteLine($"[Letterbox] Final input to YOLO: {targetWidth}x{targetHeight} (model size)");

            using var letterboxed = new SKBitmap(targetWidth, targetHeight);
            using (var canvas = new SKCanvas(letterboxed))
            {
                canvas.Clear(SKColors.Black);
                canvas.DrawBitmap(resized, padX, padY);
            }

            // --- CHW, normalized 0–1, BGR order ---
            var pixels = letterboxed.Pixels;
            var inputData = new float[targetWidth * targetHeight * 3];
            int hw = targetWidth * targetHeight;

            for (int y = 0; y < targetHeight; y++)
            {
                for (int x = 0; x < targetWidth; x++)
                {
                    int idx = y * targetWidth + x;
                    var color = pixels[idx];

                    inputData[idx] = color.Blue / 255f;
                    inputData[idx + hw] = color.Green / 255f;
                    inputData[idx + 2 * hw] = color.Red / 255f;
                }
            }

            Debug.WriteLine($"[Input] Data array size: {inputData.Length} floats");
            Debug.WriteLine($"[Input] Min/Max values: {inputData.Min():F4}/{inputData.Max():F4}");

            // Cleanup rotated bitmap if created
            if (DeviceInfo.Platform == DevicePlatform.Android)
            {
                if (workingBitmap != bitmap)
                {
                    workingBitmap.Dispose();
                }
            }

            return (inputData,
                    targetWidth, targetHeight,
                    origWidth, origHeight,
                    scale, padX, padY);
        }
    }
}
