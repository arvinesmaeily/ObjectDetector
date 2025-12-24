namespace Services
{
    public record Detection(
        float X,
        float Y,
        float Width,
        float Height,
        string Label,
        float Confidence
    );

}
