using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Diagnostics;
using System.Numerics.Tensors;


namespace ObjectDetector.Services
{
    public class YoloDetector : IDisposable
    {
        private readonly InferenceSession _session;
        private readonly string _inputName;

        private const string FloatModelAssetName = "yolo11m.onnx";
        private const string Int8ModelAssetName = "yolo11m_int8.onnx";

        private static readonly string[] ClassNames = new[]
{
    "person","bicycle","car","motorcycle","airplane","bus","train","truck","boat",
    "traffic light","fire hydrant","stop sign","parking meter","bench",
    "bird","cat","dog","horse","sheep","cow","elephant","bear","zebra","giraffe",
    "backpack","umbrella","handbag","tie","suitcase","frisbee","skis","snowboard",
    "sports ball","kite","baseball bat","baseball glove","skateboard","surfboard","tennis racket",
    "bottle","wine glass","cup","fork","knife","spoon","bowl","banana","apple","sandwich","orange",
    "broccoli","carrot","hot dog","pizza","donut","cake","chair","couch","potted plant","bed",
    "dining table","toilet","tv","laptop","mouse","remote","keyboard","cell phone","microwave","oven",
    "toaster","sink","refrigerator","book","clock","vase","scissors","teddy bear","hair drier","toothbrush"
};


        public YoloDetector()
        {
            // Keep app-local copies of the packaged models. This avoids issues with native ORT APIs that need file paths.
            var floatModelPath = EnsureModelCopiedToAppData(FloatModelAssetName);
            var int8ModelPath = EnsureModelCopiedToAppData(Int8ModelAssetName);

            (_session, _inputName) = CreateSessionWithBestModel(floatModelPath, int8ModelPath);
        }

        private static string EnsureModelCopiedToAppData(string assetName)
        {
            var targetPath = Path.Combine(FileSystem.AppDataDirectory, assetName);

            using var packagedStream = FileSystem.OpenAppPackageFileAsync(assetName).Result;

            // Copy only if missing or different (length check first; then content check as needed).
            bool needsCopy = true;
            if (File.Exists(targetPath))
            {
                try
                {
                    var existingInfo = new FileInfo(targetPath);
                    if (existingInfo.Length == packagedStream.Length)
                    {
                        // Same length; assume same for now to avoid hashing overhead.
                        needsCopy = false;
                    }
                }
                catch
                {
                    needsCopy = true;
                }
            }

            if (needsCopy)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                using var fileStream = File.Create(targetPath);
                packagedStream.CopyTo(fileStream);
            }

            return targetPath;
        }

        private static (InferenceSession session, string inputName) CreateSessionWithBestModel(string floatModelPath, string int8ModelPath)
        {
            // Platform intent:
            // - Windows: prefer DirectML; INT8 model is OK.
            // - Android: prefer NNAPI; INT8 model may or may not work depending on device/driver; try it first.
            // - iOS/MacCatalyst: use CPU (CoreML EP not wired up here).

            if (DeviceInfo.Platform == DevicePlatform.WinUI)
            {
                // Attempt: INT8 + DirectML -> FP + DirectML -> FP CPU
                return TryCreateWindowsSession(int8ModelPath)
                    ?? TryCreateWindowsSession(floatModelPath)
                    ?? CreateCpuSession(floatModelPath);
            }

            if (DeviceInfo.Platform == DevicePlatform.Android)
            {
                // Attempt: INT8 + NNAPI -> FP + NNAPI -> FP CPU
                return TryCreateAndroidSession(int8ModelPath)
                    ?? TryCreateAndroidSession(floatModelPath)
                    ?? CreateCpuSession(floatModelPath);
            }

            // Default (iOS/MacCatalyst): CPU with FP model
            Debug.WriteLine("[ONNX] Using CPU");
            return CreateCpuSession(floatModelPath);
        }

        private static (InferenceSession session, string inputName)? TryCreateWindowsSession(string modelPath)
        {
            var opts = new SessionOptions();
            try
            {
                opts.AppendExecutionProvider_DML();
                var s = new InferenceSession(modelPath, opts);
                Debug.WriteLine($"[ONNX] Windows: Using DirectML with {Path.GetFileName(modelPath)}");
                return (s, s.InputMetadata.Keys.First());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ONNX] Windows: DirectML init failed for {Path.GetFileName(modelPath)}: {ex.Message}");
                opts.Dispose();
                return null;
            }
        }

        private static (InferenceSession session, string inputName)? TryCreateAndroidSession(string modelPath)
        {
            var opts = new SessionOptions();
            try
            {
                opts.AppendExecutionProvider_Nnapi();
                var s = new InferenceSession(modelPath, opts);
                Debug.WriteLine($"[ONNX] Android: Using NNAPI with {Path.GetFileName(modelPath)}");
                return (s, s.InputMetadata.Keys.First());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ONNX] Android: NNAPI init failed for {Path.GetFileName(modelPath)}: {ex.Message}");
                opts.Dispose();
                return null;
            }
        }

        private static (InferenceSession session, string inputName) CreateCpuSession(string modelPath)
        {
            var s = new InferenceSession(modelPath, new SessionOptions());
            Debug.WriteLine($"[ONNX] Using CPU with {Path.GetFileName(modelPath)}");
            return (s, s.InputMetadata.Keys.First());
        }


        public List<Detection> Detect(float[] imageData, int width, int height, float confidenceThreshold = 0.25f, float iouThreshold = 0.45f)
        {
            // YOLO input: [1,3,H,W]
            var inputDims = new[] { 1, 3, height, width };
            var tensor = new DenseTensor<float>(imageData, inputDims);

            var input = new List<NamedOnnxValue>
    {
        NamedOnnxValue.CreateFromTensor(_inputName, tensor)
    };

            using var results = _session.Run(input);

            // Get first output as Tensor so we can read real dimensions
            var outputTensor = results.First().AsTensor<float>();
            var dims = outputTensor.Dimensions.ToArray();
            Debug.WriteLine(results.First().ValueType);
            var raw = outputTensor.ToArray();

            return Decode(raw, dims, confidenceThreshold, iouThreshold);
        }

        private List<Detection> Decode(
            float[] raw, int[] dims,
            float confThreshold = 0.25f, float iouThreshold = 0.45f)
        {
            // Expect 3D: [1, C, N] or [1, N, C]
            if (dims.Length != 3)
                return [];

            int batch = dims[0];
            int d1 = dims[1];
            int d2 = dims[2];

            if (batch != 1)
                return [];

            int numBoxes, elemPerBox;
            bool boxesFirst;

            // Heuristic: channels-small, boxes-large (e.g. 84 x 8400) => channels-first
            if (d1 <= 300 && d2 > d1)
            {
                // [1, C, N]
                elemPerBox = d1;   // C = 84
                numBoxes = d2;     // N = 8400
                boxesFirst = false;
            }
            else
            {
                // [1, N, C]
                numBoxes = d1;     // N
                elemPerBox = d2;   // C
                boxesFirst = true;
            }

            var detections = new List<Detection>(numBoxes);

            // Case 1: NMS ON – [x1,y1,x2,y2,score,classId]
            if (elemPerBox == 6)
            {
                Debug.WriteLine("Using NMS case (6 elements per box)");

                if (boxesFirst)
                {
                    // [1, N, 6]
                    for (int i = 0; i < numBoxes; i++)
                    {
                        int offset = i * elemPerBox;
                        float x1 = raw[offset + 0];
                        float y1 = raw[offset + 1];
                        float x2 = raw[offset + 2];
                        float y2 = raw[offset + 3];
                        float score = raw[offset + 4];
                        int cls = (int)raw[offset + 5];

                        if (score < confThreshold)
                            continue;

                        float w = x2 - x1;
                        float h = y2 - y1;

                        string label = (cls >= 0 && cls < ClassNames.Length)
                            ? ClassNames[cls]
                            : $"cls_{cls}";

                        detections.Add(new Detection(
                            X: x1,
                            Y: y1,
                            Width: w,
                            Height: h,
                            Label: label,
                            Confidence: score));
                    }
                }
                else
                {
                    // [1, 6, N]
                    int N = numBoxes;

                    for (int i = 0; i < N; i++)
                    {
                        float x1 = raw[0 * N + i];
                        float y1 = raw[1 * N + i];
                        float x2 = raw[2 * N + i];
                        float y2 = raw[3 * N + i];
                        float score = raw[4 * N + i];
                        int cls = (int)raw[5 * N + i];

                        if (score < confThreshold)
                            continue;

                        float w = x2 - x1;
                        float h = y2 - y1;

                        string label = (cls >= 0 && cls < ClassNames.Length)
                            ? ClassNames[cls]
                            : $"cls_{cls}";

                        detections.Add(new Detection(
                            X: x1,
                            Y: y1,
                            Width: w,
                            Height: h,
                            Label: label,
                            Confidence: score));
                    }
                }

                // Already NMS'ed by the exported model
                return detections;
            }

            // Case 2: Standard YOLO – [x,y,w,h,obj,cls0..clsN]
            if (elemPerBox < 5)
                return [];

            int numClasses = elemPerBox - 5;   // 4 box + 1 obj + classes
            Debug.WriteLine($"Standard YOLO: numClasses={numClasses}, elemPerBox={elemPerBox}");

            if (numBoxes <= 0 || numClasses <= 0)
                return [];

            if (boxesFirst)
            {
                // [1, N, C] → raw index: i * C + c
                for (int i = 0; i < numBoxes; i++)
                {
                    int offset = i * elemPerBox;

                    float x = raw[offset + 0];
                    float y = raw[offset + 1];
                    float w = raw[offset + 2];
                    float h = raw[offset + 3];
                    float obj = raw[offset + 4];

                    int bestClass = -1;
                    float bestScore = 0f;

                    for (int c = 0; c < numClasses; c++)
                    {
                        float clsProb = raw[offset + 5 + c];
                        float score = obj * clsProb;

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestClass = c;
                        }
                    }

                    if (bestScore < confThreshold)
                        continue;

                    float x1 = x - w / 2f;
                    float y1 = y - h / 2f;

                    string label = (bestClass >= 0 && bestClass < ClassNames.Length)
                        ? ClassNames[bestClass]
                        : $"cls_{bestClass}";

                    detections.Add(new Detection(
                        X: x1,
                        Y: y1,
                        Width: w,
                        Height: h,
                        Label: label,
                        Confidence: bestScore));
                }
            }
            else
            {
                // [1, C, N] → raw index: c * N + i
                // YOLOv11 format: [x, y, w, h, cls0, cls1, ..., cls79]
                // No separate objectness score!
                int N = numBoxes;

                for (int i = 0; i < N; i++)
                {
                    float x = raw[0 * N + i];
                    float y = raw[1 * N + i];
                    float w = raw[2 * N + i];
                    float h = raw[3 * N + i];

                    int bestClass = -1;
                    float bestScore = 0f;

                    // Channels 4-83 are the 80 class probabilities (but we only have 79?)
                    // Try starting from channel 4
                    for (int c = 4; c < elemPerBox; c++)
                    {
                        float clsProb = raw[c * N + i];

                        if (clsProb > bestScore)
                        {
                            bestScore = clsProb;
                            bestClass = c - 4;
                        }
                    }

                    if (bestScore < confThreshold)
                        continue;

                    float x1 = x - w / 2f;
                    float y1 = y - h / 2f;

                    string label = (bestClass >= 0 && bestClass < ClassNames.Length)
                        ? ClassNames[bestClass]
                        : $"cls_{bestClass}";

                    detections.Add(new Detection(
                        X: x1,
                        Y: y1,
                        Width: w,
                        Height: h,
                        Label: label,
                        Confidence: bestScore));
                }
            }

            return NonMaxSuppression(detections, iouThreshold);
        }

        private static List<Detection> NonMaxSuppression(
            List<Detection> detections, float iouThreshold)
        {
            var result = new List<Detection>();
            var sorted = detections
                .OrderByDescending(d => d.Confidence)
                .ToList();

            while (sorted.Count > 0)
            {
                var current = sorted[0];
                result.Add(current);
                sorted.RemoveAt(0);

                sorted = sorted
                    .Where(d => IoU(current, d) < iouThreshold)
                    .ToList();
            }

            return result;
        }

        private static float IoU(Detection a, Detection b)
        {
            float x1 = Math.Max(a.X, b.X);
            float y1 = Math.Max(a.Y, b.Y);
            float x2 = Math.Min(a.X + a.Width, b.X + b.Width);
            float y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);

            float interArea = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
            if (interArea <= 0) return 0f;

            float areaA = a.Width * a.Height;
            float areaB = b.Width * b.Height;

            return interArea / (areaA + areaB - interArea);
        }


        public void Dispose()
        {
            _session.Dispose();
            GC.SuppressFinalize(this);
        }
    }


}
