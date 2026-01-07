namespace ObjectDetector.Services
{
    public static class DetectionRenderingConfig
    {
        // Reference size for normalization
        private const float BaseImageSize = 640f;
        
        // Base values that scale with image size
        private const float BaseFontSize = 20f;
        private const float BaseStrokeWidth = 2f;
        private const float BasePaddingX = 8f;
        private const float BasePaddingY = 6f;

        // For UI overlays - scale based on display size
        public static ScaleStruct GetScaledValues(
            float displayWidth, 
            float displayHeight)
        {
            float uiScaleFactor = Preferences.Get("UiScaleFactor", 0.5f);
            
            // Scale based on display size (how big it appears on screen)
            float displaySizeSq = displayWidth * displayHeight;
            float scaleFactor = displaySizeSq / float.Pow(BaseImageSize, 2) * uiScaleFactor;

            return new ScaleStruct(
                BaseFontSize *  scaleFactor,
                BaseStrokeWidth * scaleFactor,
                BasePaddingX * scaleFactor,
                BasePaddingY * scaleFactor
            );
        }

        // For saved images - scale based on IMAGE SIZE (larger images = larger text)
        public static ScaleStruct GetScaledValuesForSavedImage(int imageWidth, int imageHeight)
        {
            float uiScaleFactor = Preferences.Get("UiScaleFactor", 0.5f);
            
            // DYNAMIC SCALING: Scale based on actual image size
            // Larger images (3000px) get larger text than smaller images (800px)
            float imageSizeSq = imageWidth * imageHeight;
            float scaleFactor = imageSizeSq / float.Pow(BaseImageSize, 2) * uiScaleFactor;

            return new ScaleStruct(
                BaseFontSize * scaleFactor,
                BaseStrokeWidth * scaleFactor,
                BasePaddingX * scaleFactor,
                BasePaddingY * scaleFactor
            );
        }

        // Legacy overload
        public static ScaleStruct GetScaledValues(int imageWidth, int imageHeight)
        {
            return GetScaledValuesForSavedImage(imageWidth, imageHeight);
        }
    }

    public record struct ScaleStruct(float FontSize, float StrokeWidth, float PaddingX, float PaddingY)
    {
        public static implicit operator (float fontSize, float strokeWidth, float paddingX, float paddingY)(ScaleStruct value)
        {
            return (value.FontSize, value.StrokeWidth, value.PaddingX, value.PaddingY);
        }

        public static implicit operator ScaleStruct((float fontSize, float strokeWidth, float paddingX, float paddingY) value)
        {
            return new ScaleStruct(value.fontSize, value.strokeWidth, value.paddingX, value.paddingY);
        }
    }
}
