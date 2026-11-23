using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>The world marks that the Tab map and the planning screen hide while they are open (P-UI-TABMAP-SHEET-1).</summary>
    public sealed partial class ArenaView
    {
        static readonly string[] MapHiddenRoots = { "World markers", "World indicators", "Quest carrier flags" };

        /// <summary>Shows or hides the roots of the world markers, world indicators and quest carrier flags (found by name; none is created here).</summary>
        public void SetWorldMarksVisible(bool visible)
        {
            foreach (string name in MapHiddenRoots)
            {
                var root = transform.Find(name);
                if (root != null && root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
            }
        }
    }
}
