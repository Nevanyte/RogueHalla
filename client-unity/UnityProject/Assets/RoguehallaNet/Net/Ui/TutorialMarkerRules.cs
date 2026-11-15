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
        public static bool CardVisible(bool confirmedPractice, bool windowFocused, bool higherPanelVisible, bool flowActive) { return false; }
        public static bool WelcomeVisible(bool tutorialDue, bool menuVisible, bool dismissedThisSession, bool inMatch) { return false; }
        public static string WelcomeAction(AccountTutorialState stored, int currentVersion) { return null; }
        public static MarkerPlacement Place(float screenX, float screenY, bool inFront, float width, float height, float margin) { return default(MarkerPlacement); }
        public static string StepLabel(int index, int total, string copy, string wishKey) { return null; }
    }
}
