<p align="center">
  <img src="https://github.com/arvinesmaeily/ObjectDetector/blob/master/ObjectDetector/Resources/AppIcon/icon_full.png" width="80" alt="ObjectDetector Icon">
</p>
<h1 align="center">ObjectDetector</h1>

<div style="display: flex; gap: 10px; flex-wrap: wrap;">
    <img src="https://img.shields.io/github/license/arvinesmaeily/ObjectDetector" alt="License">
    <img src="https://img.shields.io/github/last-commit/arvinesmaeily/ObjectDetector" alt="Last Commit">
    <img src="https://img.shields.io/github/issues/arvinesmaeily/ObjectDetector" alt="Open Issues">
    <img src="https://img.shields.io/badge/.NET-10.0-blue" alt=".NET 10">
    <img src="https://img.shields.io/badge/MAUI-Cross--Platform-purple" alt="MAUI">
</div>
<br/>

A cross-platform .NET MAUI application that performs real-time object detection using YOLOv11 models. Built with ONNX Runtime for hardware-accelerated inference on Windows, Android, iOS, and macOS.

This project leverages [ONNX Runtime](https://github.com/microsoft/onnxruntime) for efficient neural network inference and [SkiaSharp](https://github.com/mono/SkiaSharp) for high-performance graphics rendering.

## ✨ Features

* **Real-Time Camera Detection**: Detect 80+ object classes in real-time using your device's camera
* **Gallery Image Analysis**: Process and analyze images from your photo gallery
* **Multi-Platform Support**: Runs on Windows, Android, iOS, and macOS (Catalyst)
* **Hardware Acceleration**: Automatically uses DirectML (Windows) or NNAPI (Android) when available
* **YOLOv11 Models**: Includes both float32 and int8 quantized models for optimal performance
* **Customizable Detection**: Adjust confidence and IoU thresholds in real-time
* **Visual Overlays**: Color-coded bounding boxes with confidence scores
* **Image Capture**: Save detection results with annotations to your device
* **Light & Dark Themes**: Automatically adapts to system theme preferences
* **FPS Counter**: Monitor real-time detection performance

---

## 🏗️ Project Structure

```
ObjectDetector/
├── Pages/
│   ├── CameraPage.xaml.cs      # Real-time camera detection
│   ├── GalleryPage.xaml.cs     # Static image processing
│   ├── SettingsPage.xaml.cs    # App configuration
│   └── HistoryPage.xaml.cs     # (Future feature)
├── Services/
│   ├── YoloDetector.cs         # ONNX Runtime inference engine
│   ├── Detection.cs            # Detection result model
│   ├── DetectionDrawable.cs    # Real-time overlay rendering
│   └── DetectionRenderingConfig.cs # Adaptive UI scaling
├── Resources/
│   └── Raw/
│       ├── yolo11m.onnx        # Float32 YOLOv11-medium model
│       └── yolo11m_int8.onnx   # INT8 quantized model
├── Platforms/
│   ├── Android/                # Android-specific implementations
│   ├── iOS/                    # iOS-specific implementations
│   ├── MacCatalyst/           # macOS-specific implementations
│   └── Windows/               # Windows-specific implementations
├── AppShell.xaml              # App navigation structure
└── MauiProgram.cs             # App initialization & DI setup
```

---

## 🚀 Getting Started

### Prerequisites

#### Development Environment
* **Visual Studio 2022** (version 17.13 or newer) with .NET MAUI workload installed
* **.NET 10 SDK** or newer
* **Platform-specific SDKs**:
  - **Windows**: Windows 10 SDK (19041 or newer)
  - **Android**: Android SDK API 26+ (Android 8.0 Oreo)
  - **iOS**: Xcode 15.0+ (iOS 15.0+)
  - **macOS**: Xcode 15.0+ (macOS 12.0+)

#### Runtime Requirements
* **Display**: Minimum 1280x720 resolution recommended
* **Camera**: Required for real-time detection mode
* **Storage**: ~500 MB for app and models

### Installation

#### Option 1: Build from Source

1. Clone the repository:
   ```bash
   git clone https://github.com/arvinesmaeily/ObjectDetector.git
   cd ObjectDetector
   ```

2. Open `ObjectDetector.sln` in Visual Studio 2022

3. Select your target platform (Android, iOS, Windows, or macOS)

4. Build and run the project (F5)

#### Option 2: Download Release (Coming Soon)

1. Navigate to the [**Releases**](https://github.com/arvinesmaeily/ObjectDetector/releases) page
2. Download the installer for your platform
3. Install and run the application

---

## 🔧 Usage Guide

### 1. Camera Mode

Perform real-time object detection using your device's camera.

1. Navigate to the **Camera** tab
2. Grant camera permissions when prompted
3. Point your camera at objects to detect them in real-time
4. **Toggle Detections**: Use the 👁️ button to show/hide bounding boxes
5. **Capture**: Click the capture button to save the current frame with annotations
6. **Monitor Performance**: View FPS and detection count in the top-right corner

**Tips:**
- Ensure adequate lighting for better detection accuracy
- Keep objects within the camera's field of view
- Adjust detection thresholds in Settings for optimal results

### 2. Gallery Mode

Analyze static images from your device's photo library.

1. Navigate to the **Gallery** tab
2. Click **Pick Image** to select a photo
3. Wait for processing to complete
4. View detected objects with bounding boxes and labels
5. Click **Save** to export the annotated image

### 3. Settings

Customize the app's behavior and detection parameters.

* **Confidence Threshold** (0.0 - 1.0): Minimum confidence for displaying detections (default: 0.25)
* **IoU Threshold** (0.0 - 1.0): Overlap threshold for Non-Maximum Suppression (default: 0.45)
* **UI Scale Factor** (0.1 - 1.0): Adjust overlay text and bounding box sizes
* **Theme**: Choose between System, Light, or Dark mode
* **Keep Screen Alive**: Prevent screen timeout during camera use
* **Open Folder**: Access saved detection images

---

## 🖥️ Supported Platforms

| Platform | Status | Minimum Version | Hardware Acceleration |
|----------|--------|----------------|----------------------|
| **Windows** | ✅ Supported | Windows 10 (19041) | DirectML (GPU) |
| **Android** | ✅ Supported | Android 8.0 (API 26) | NNAPI |
| **iOS** | ✅ Supported | iOS 15.0+ | CPU (CoreML future) |
| **macOS** | ✅ Supported | macOS 12.0+ (Catalyst) | CPU (CoreML future) |

---

## 📦 Runtime Dependencies

### All Platforms
* **.NET 10 Runtime**
* **Microsoft.Maui** (10.0.20)
* **Microsoft.ML.OnnxRuntime.Managed** (1.23.2)
* **SkiaSharp** (3.119.1) - Graphics rendering
* **CommunityToolkit.Maui** (13.0.0) - MAUI extensions
* **CommunityToolkit.Maui.Camera** (5.0.0) - Camera access

### Platform-Specific

#### Windows
* **Microsoft.ML.OnnxRuntime.DirectML** (1.23.0) - GPU acceleration via DirectX 12

#### Android
* **NNAPI Support** (included in ONNX Runtime)
* Camera2 API support
* External storage permissions for saving images

#### iOS/macOS
* **AVFoundation** (built-in) - Camera access
* **Photos Framework** (built-in) - Gallery access

---

## ⚙️ Hardware/Software Requirements

### Minimum Requirements

| Component | Specification |
|-----------|--------------|
| **CPU** | Dual-core 1.5 GHz or equivalent |
| **RAM** | 2 GB (4 GB recommended) |
| **Storage** | 500 MB available space |
| **Camera** | 5 MP or higher (for Camera mode) |
| **OS** | See platform-specific versions above |

### Recommended Requirements

| Component | Specification |
|-----------|--------------|
| **CPU** | Quad-core 2.0 GHz or better |
| **RAM** | 4 GB or more |
| **GPU** | DirectX 12 compatible (Windows) or equivalent |
| **Camera** | 8 MP or higher with autofocus |
| **Display** | 1920x1080 or higher |

### GPU Acceleration

- **Windows**: Requires DirectX 12 compatible GPU (most GPUs from 2016+)
- **Android**: Requires NNAPI-compatible hardware (most devices from 2018+)
- **iOS/macOS**: CPU inference only (CoreML support planned)

---

## 🧠 Model Information

### YOLOv11-Medium

The app includes two variants of the YOLOv11-medium model:

* **yolo11m.onnx** (Float32): Higher accuracy, larger file size (~50 MB)
* **yolo11m_int8.onnx** (INT8 Quantized): Faster inference, smaller size (~25 MB)

The app automatically selects the best model based on platform capabilities:
- **Windows**: Prefers INT8 + DirectML, falls back to Float32
- **Android**: Prefers INT8 + NNAPI, falls back to Float32 on CPU
- **iOS/macOS**: Uses Float32 on CPU

### Detected Object Classes (80 COCO Classes)

The model can detect 80 common objects including:
- **People**: person
- **Vehicles**: car, truck, bus, motorcycle, bicycle, airplane, train, boat
- **Animals**: dog, cat, bird, horse, sheep, cow, elephant, bear, zebra, giraffe
- **Indoor Objects**: chair, couch, bed, dining table, tv, laptop, cell phone, book
- **Kitchen Items**: bottle, cup, fork, knife, spoon, bowl, banana, apple, sandwich
- And many more...

---

## 🐞 Troubleshooting

### Camera Not Working
- Ensure camera permissions are granted in app settings
- Check that no other app is using the camera
- Restart the application

### Low FPS on Android
- The app will automatically use NNAPI if available
- Try adjusting the confidence threshold higher to reduce detections
- Close background apps to free up resources

### Detection Quality Issues
- Adjust the **Confidence Threshold** in Settings (lower = more detections)
- Adjust the **IoU Threshold** for overlapping detections
- Ensure adequate lighting and clear view of objects

### Saved Images Not Appearing
- **Android**: Check Pictures/ObjectDetector folder
- **Windows**: Check Pictures/ObjectDetector folder
- **iOS/macOS**: Use Settings > Open Folder to view location

---

## 🐞 Reporting Issues

If you encounter a bug or have a suggestion, please [open an issue](https://github.com/arvinesmaeily/ObjectDetector/issues) on the repository.

When reporting detection or performance problems, please include:
- Your device/platform (e.g., "Windows 11, RTX 3060")
- App version
- Steps to reproduce the issue
- Screenshots if applicable

---

## 🛣️ Roadmap

- [ ] History page to view saved detections
- [ ] CoreML execution provider for iOS/macOS
- [ ] Batch processing for multiple images
- [ ] Model download/update mechanism

---

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the project
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

---

## 🙏 Acknowledgments

* [ONNX Runtime](https://github.com/microsoft/onnxruntime) - High-performance inference engine
* [Ultralytics YOLOv11](https://github.com/ultralytics/ultralytics) - State-of-the-art object detection models
* [SkiaSharp](https://github.com/mono/SkiaSharp) - Cross-platform 2D graphics
* [.NET MAUI Community Toolkit](https://github.com/CommunityToolkit/Maui) - Essential MAUI extensions
* COCO Dataset - Training data for object detection models

---

## 📄 License

This work is under an [MIT](https://choosealicense.com/licenses/mit/) License. Visit [LICENSE](https://github.com/arvinesmaeily/ObjectDetector/blob/master/LICENSE.txt) for more info.
