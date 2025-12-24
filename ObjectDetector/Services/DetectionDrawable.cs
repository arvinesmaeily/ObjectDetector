using Microsoft.Maui.Graphics;
using Services;
using System.Diagnostics;

public class DetectionDrawable : IDrawable
{
    private IReadOnlyList<Detection> _detections = Array.Empty<Detection>();
    private int _imageWidth;
    private int _imageHeight;
    private bool _showDetections = true;

    public void UpdateDetections(IReadOnlyList<Detection> detections, int imageWidth, int imageHeight)
    {
        _detections = detections ?? Array.Empty<Detection>();
        _imageWidth = imageWidth;
        _imageHeight = imageHeight;
    }

    public void SetShowDetections(bool show)
    {
        _showDetections = show;
    }

    public IReadOnlyList<Detection> GetCurrentDetections()
    {
        return _detections;
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (!_showDetections || _detections == null || _detections.Count == 0 || _imageWidth == 0 || _imageHeight == 0)
        {
            return;
        }

        if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0 || _imageWidth <= 0 || _imageHeight <= 0)
            return;

        // Scale to fit the available canvas while maintaining aspect ratio
        float scale = Math.Min(
            dirtyRect.Width / _imageWidth,
            dirtyRect.Height / _imageHeight);

        float drawWidth = _imageWidth * scale;
        float drawHeight = _imageHeight * scale;

        // Center the scaled image in the canvas
        float offsetX = dirtyRect.X + (dirtyRect.Width - drawWidth) / 2f;
        float offsetY = dirtyRect.Y + (dirtyRect.Height - drawHeight) / 2f;

        float fontSize = 12f;
        IFont font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.Font = font;
        canvas.FontSize = fontSize;

        foreach (var det in _detections)
        {
            // Convert detection coords to canvas coords
            float x = offsetX + (det.X * scale);
            float y = offsetY + (det.Y * scale);
            float w = det.Width * scale;
            float h = det.Height * scale;

            // Draw bounding box
            canvas.StrokeSize = 2f;
            canvas.StrokeColor = Colors.Lime;
            canvas.StrokeDashPattern = null;
            canvas.DrawRectangle(x, y, w, h);

            // Draw label with background
            string text = $"{det.Label} {det.Confidence:P0}";
            var textSize = canvas.GetStringSize(text, font, fontSize);

            float paddingX = 4f;
            float paddingY = 2f;

            float bgX = x;
            float bgY = Math.Max(0, y - (textSize.Height + paddingY * 2));
            float bgWidth = textSize.Width + paddingX * 2;
            float bgHeight = textSize.Height + paddingY * 2;

            // Label background
            canvas.FillColor = Colors.Lime;
            canvas.FillRectangle(bgX, bgY, bgWidth, bgHeight);

            // Label text
            canvas.FontColor = Colors.Black;
            canvas.DrawString(
                text,
                bgX + paddingX,
                bgY + paddingY,
                bgWidth,
                bgHeight,
                HorizontalAlignment.Left,
                VerticalAlignment.Top);
        }
    }
}
