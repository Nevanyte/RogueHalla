using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>The quest tracker: the accepted quests that are still running, docked under the plate chain. It draws
    /// delivered facts and takes no input.</summary>
    public sealed class QuestTrackerView
    {
        public QuestTrackerView(Transform parent)
        {
            Root = HudUi.Box(parent, "QuestTracker", HudAnchor.TopLeft, 12f, 0f, 260f, 0f);
            Root.gameObject.SetActive(false);
        }

        public RectTransform Root { get; }
        /// <summary>Rows wanted and showing; 0 while hidden or leaving.</summary>
        public int Rows { get { return 0; } }
        public void Refresh(Session session, bool hudVisible, float dockTop, float dockFloor, float dt) { }
    }
}
