namespace Roguehalla.Net.Ui
{
    /// <summary>Where a tutorial guide marker sits. Screen units, origin top-left, y down; Angle = atan2(dy, dx) of the ray from the centre.</summary>
    public readonly struct MarkerPlacement
    {
        public bool OnScreen { get; }
        public float X { get; }
        public float Y { get; }
        public float Angle { get; }
        public MarkerPlacement(bool onScreen, float x, float y, float angle) { OnScreen = onScreen; X = x; Y = y; Angle = angle; }
    }

    /// <summary>Pure presentation rules of the tutorial cards and guide marker (P-TUTORIAL-3). Presentation only; no engine types.</summary>
    public static class TutorialMarkerRules
    {
        public static bool CardVisible(bool confirmedPractice, bool windowFocused, bool higherPanelVisible, bool flowActive)
        { return confirmedPractice && windowFocused && !higherPanelVisible && flowActive; }

        public static bool WelcomeVisible(bool tutorialDue, bool menuVisible, bool dismissedThisSession, bool inMatch)
        { return tutorialDue && menuVisible && !dismissedThisSession && !inMatch; }

        public static string WelcomeAction(AccountTutorialState stored, int currentVersion)
        { return stored != null && stored.State == "step" && stored.Version == currentVersion && stored.Step > 0 ? "Resume" : "Start"; }

        public static MarkerPlacement Place(float screenX, float screenY, bool inFront, float width, float height, float margin)
        {
            if (inFront && screenX >= 0f && screenX <= width && screenY >= 0f && screenY <= height)
                return new MarkerPlacement(true, screenX, screenY, 0f);
            float cx = width * 0.5f, cy = height * 0.5f;
            float dx = screenX - cx, dy = screenY - cy;
            if (!inFront) { dx = -dx; dy = -dy; }
            if (System.Math.Abs(dx) < 1e-4f && System.Math.Abs(dy) < 1e-4f)
                return new MarkerPlacement(false, cx, height - margin, (float)(System.Math.PI / 2));
            float hw = cx - margin, hh = cy - margin;
            float tx = System.Math.Abs(dx) > 1e-6f ? hw / System.Math.Abs(dx) : float.MaxValue;
            float ty = System.Math.Abs(dy) > 1e-6f ? hh / System.Math.Abs(dy) : float.MaxValue;
            float t = System.Math.Min(tx, ty);
            return new MarkerPlacement(false, cx + dx * t, cy + dy * t, (float)System.Math.Atan2(dy, dx));
        }

        public static string StepLabel(int index, int total, string copy, string wishKey)
        {
            string text = (copy ?? string.Empty).Replace("{wish}", wishKey ?? string.Empty);
            return index >= 1 && index <= total ? "Step " + index + "/" + total + " \u00B7 " + text : text;
        }
    }
}
