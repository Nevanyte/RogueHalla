using System.Globalization;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net.Ui
{
    /// <summary>The journal's words for a quest: the state on its list row, the status line, the reason it cannot be
    /// accepted. Every input is a delivered fact.</summary>
    public static class QuestJournalCopy
    {
        /// <summary>The server's accept range in world units: used only to choose the hint's wording.</summary>
        public const double GiverReach = 64d;

        /// <summary>The state word on a list row.</summary>
        public static string RowState(QuestRowView q)
        {
            if (q.OwnerCompleted) return "COMPLETE";
            if (q.State == QuestRowState.Expired) return "EXPIRED";
            if (q.State == QuestRowState.Done) return "DONE";
            if (q.OwnerAccepted) return q.OwnerReady ? "TURN IN" : "ACCEPTED";
            if (q.State == QuestRowState.Preview) return "PREVIEW";
            return q.CanAccept ? "OFFER · HERE" : "OFFER";
        }

        /// <summary>The status line of the selected quest; secondsLeft is -1 without a deadline.</summary>
        public static string Status(QuestRowView q, bool acceptPending, bool rewardPending, int secondsLeft)
        {
            if (acceptPending) return "Accepting…";
            if (rewardPending) return "Choose your reward";
            if (q.OwnerCompleted) return "Completed";
            if (q.State == QuestRowState.Expired) return "Expired";
            if (q.State == QuestRowState.Done) return "Finished";
            string time = secondsLeft < 0 ? "" : secondsLeft == 0 ? " · time is up" : " · " + QuestTracker.Clock(secondsLeft) + " left";
            if (q.OwnerAccepted && q.OwnerReady) return "Ready to turn in" + time;
            if (q.OwnerAccepted)
            {
                int n = QuestTracker.DwellSeconds(q.Def);
                string progress = n > 0
                    ? q.OwnerProgress.ToString(CultureInfo.InvariantCulture) + " / " + n.ToString(CultureInfo.InvariantCulture) + " s"
                    : q.OwnerProgress.ToString(CultureInfo.InvariantCulture) + " progress";
                return "Accepted · " + progress + time;
            }
            if (q.State == QuestRowState.Preview) return "Preview" + time;
            if (q.CanAccept) return "Offer" + time;
            return "Offer · not in reach" + time;
        }

        /// <summary>Why an open offer cannot be accepted, or "" when there is nothing to say.</summary>
        public static string Hint(QuestRowView q, double distanceToGiver)
        {
            if (q.State != QuestRowState.Open || q.CanAccept || q.OwnerAccepted || q.OwnerCompleted) return "";
            if (double.IsNaN(distanceToGiver) || double.IsInfinity(distanceToGiver) || distanceToGiver > GiverReach)
            {
                string giver = string.IsNullOrWhiteSpace(q.GiverLabel) ? "the quest giver" : q.GiverLabel;
                return "Go to " + giver + " to accept";
            }
            return "Cannot be accepted right now";
        }

        /// <summary>False when the brief is empty or only repeats the objective.</summary>
        public static bool ShowsBrief(QuestRowView q)
        {
            if (string.IsNullOrWhiteSpace(q.Brief)) return false;
            return !string.Equals(q.Brief.Trim(), (q.Objective ?? "").Trim(), System.StringComparison.Ordinal);
        }
    }
}
