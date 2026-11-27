using System.Text;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net.Ui
{
    /// <summary>Compact directions for quests on the map, built only from the quest facts delivered to this viewer.</summary>
    public static class QuestMapGuide
    {
        /// <summary>Writes one compact direction block for every open quest. Hidden objectives are never inferred.</summary>
        public static string Copy(QuestView view)
        {
            if (view == null || !view.Available) return "Quest directions arrive with the next server update.";
            var text = new StringBuilder();
            int shown = 0;
            for (int i = 0; i < view.Rows.Count; i++)
            {
                var quest = view.Rows[i];
                if (quest.State != QuestRowState.Open || quest.OwnerCompleted) continue;
                if (shown++ > 0) text.Append("\n\n");
                text.Append(quest.Name ?? "Quest").Append('\n');
                string giver = string.IsNullOrWhiteSpace(quest.GiverLabel) ? "quest giver" : quest.GiverLabel;
                if (!quest.OwnerAccepted)
                {
                    text.Append("ACCEPT · Visit ").Append(giver);
                    continue;
                }
                if (quest.OwnerReady)
                {
                    text.Append("TURN IN · Return to ").Append(giver);
                    continue;
                }
                text.Append("NEXT · ").Append(string.IsNullOrWhiteSpace(quest.Objective) ? "Continue the quest" : quest.Objective);
                int marks = ObjectiveCount(view, quest.Handle);
                if (marks > 0) text.Append("\nMAP · ").Append(marks).Append(marks == 1 ? " quest mark" : " quest marks");
                string progress = QuestTracker.Progress(quest);
                if (!string.IsNullOrEmpty(progress)) text.Append("\nPROGRESS · ").Append(progress);
            }
            return shown == 0 ? "No open quests delivered." : text.ToString();
        }

        static int ObjectiveCount(QuestView view, QuestHandle handle)
        {
            int count = 0;
            for (int i = 0; i < view.Objectives.Count; i++)
                if (view.Objectives[i].Quest.Equals(handle)) count++;
            return count;
        }
    }
}
