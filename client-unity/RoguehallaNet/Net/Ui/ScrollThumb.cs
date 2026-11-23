namespace Roguehalla.Net.Ui
{
    /// <summary>Where the handle of a scroll position indicator stands on its track, as fractions of the track from its top.</summary>
    public static class ScrollThumb
    {
        public const float MinLength = .06f, Slack = .5f;

        public static bool Compute(float viewport, float content, float normalized, out float top, out float bottom)
        {
            top = 0f; bottom = 0f;
            return false;
        }
    }
}
