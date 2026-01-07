using Microsoft.Maui.Graphics;

namespace ObjectDetector.Services
{
    public class DetectionDrawable : IDrawable
    {
        private IReadOnlyList<Detection> _detections = [];
        private int _imageWidth;
        private int _imageHeight;
        private bool _showDetections = true;
        private static readonly Dictionary<string, Color> _colorCache = [];

        public void UpdateDetections(IReadOnlyList<Detection> detections, int imageWidth, int imageHeight)
        {
            _detections = detections ?? [];
            _imageWidth = imageWidth;
            _imageHeight = imageHeight;
        }

        public void SetShowDetections(bool show) => _showDetections = show;

        public IReadOnlyList<Detection> GetCurrentDetections() => _detections;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (!_showDetections || _detections.Count == 0 || _imageWidth <= 0 || _imageHeight <= 0)
                return;

            if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
                return;

            float scale = Math.Min(dirtyRect.Width / _imageWidth, dirtyRect.Height / _imageHeight);

            float drawWidth = _imageWidth * scale;
            float drawHeight = _imageHeight * scale;

            if (drawWidth <= 0 || drawHeight <= 0)
                return;

            float offsetX = dirtyRect.X + (dirtyRect.Width - drawWidth) / 2f;
            float offsetY = dirtyRect.Y + (dirtyRect.Height - drawHeight) / 2f;

            var (fontSize, strokeWidth, paddingX, paddingY) = DetectionRenderingConfig.GetScaledValues(drawWidth, drawHeight);

            if (fontSize <= 0 || strokeWidth <= 0)
                return;

            var font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.Font = font;
            canvas.FontSize = fontSize;

            foreach (var det in _detections)
            {
                float x = offsetX + (det.X * scale);
                float y = offsetY + (det.Y * scale);
                float w = det.Width * scale;
                float h = det.Height * scale;

                var color = GetColorForLabel(det.Label);

                // Draw bounding box
                canvas.StrokeSize = strokeWidth;
                canvas.StrokeColor = color;
                canvas.DrawRectangle(x, y, w, h);

                // Draw label
                string text = $"{det.Label} {det.Confidence:P0}";
                var textSize = canvas.GetStringSize(text, font, fontSize);

                float bgX = x;
                float bgY = y - (textSize.Height + paddingY * 2);
                
                // If label would go off top, put it inside the box
                if (bgY < offsetY)
                    bgY = y + paddingY;

                float bgWidth = textSize.Width + paddingX * 2;
                float bgHeight = textSize.Height + paddingY * 2;

                // Draw label background with same color
                canvas.FillColor = color;
                canvas.FillRectangle(bgX, bgY, bgWidth, bgHeight);

                // Draw label text in white for contrast
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
