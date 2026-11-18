using NUnit.Framework;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    public sealed class TutorialMarkerRulesFixTests
    {
        [Test]
        public void DoneCardShowsInPracticeAfterCompletionUntilClosed()
        {
            Assert.IsTrue(TutorialMarkerRules.DoneCardVisible(true, true, false, true, false));
            Assert.IsFalse(TutorialMarkerRules.DoneCardVisible(true, true, false, true, true), "closed by the player");
            Assert.IsFalse(TutorialMarkerRules.DoneCardVisible(true, true, false, false, false), "flow not done");
        }

        [Test]
        public void DoneCardHidesWhenPracticeEndsOrIsCoveredOrUnfocused()
        {
            Assert.IsFalse(TutorialMarkerRules.DoneCardVisible(false, true, false, true, false), "practice ended");
            Assert.IsFalse(TutorialMarkerRules.DoneCardVisible(true, false, false, true, false), "window unfocused");
            Assert.IsFalse(TutorialMarkerRules.DoneCardVisible(true, true, true, true, false), "a higher panel is open");
        }

        [Test]
        public void ArmingRequiresAStartedLaunch()
        {
            Assert.IsTrue(TutorialMarkerRules.ShouldArm(true));
            Assert.IsFalse(TutorialMarkerRules.ShouldArm(false), "launch did not start: no arm, no saved step");
        }

        [Test]
        public void DoneCardShowsTheCopyAloneWithoutStepPrefix()
        {
            const string copy = "Tutorial complete. Enter the Arena when ready.";
            Assert.AreEqual(copy, TutorialMarkerRules.StepLabel(7, 5, copy, "E"));
        }
    }
}
