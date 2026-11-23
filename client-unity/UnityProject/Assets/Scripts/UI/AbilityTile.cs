using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>One 64-unit ability tile of the action row: key cap, name, countdown, cooldown wedge, charge badge.</summary>
    public sealed class AbilityTile
    {
        // Stub (P-UI-ABILITY-ROW-1): the implementer builds the tile and draws the faces.
        double _readyAtMs, _totalMs;

        public AbilityTile(Transform parent, string name, HudRegion region, float raise, string keyCap, Transform hintOverlay) { }

        public RectTransform Root { get { return null; } }
        /// <summary>Carries the hover / keyboard hint; it sends nothing.</summary>
        public UnityEngine.UI.Button Button { get { return null; } }
        public HudIconHint Hint { get { return null; } }
        public TMP_Text KeyLabel { get { return null; } }
        public TMP_Text NameLabel { get { return null; } }
        public TMP_Text SecondsLabel { get { return null; } }
        public TMP_Text BadgeLabel { get { return null; } }
        public UnityEngine.UI.Image Sweep { get { return null; } }

        /// <summary>Draws a face. Writes only what differs from what the tile shows: a call with the same face, name and hint
        /// changes nothing and allocates nothing.</summary>
        public void Show(AbilityFace face, string name, string hint) { }

        /// <summary>Remembers the running cooldown (server ms it ends at, and its length; 0 and 0 when none).</summary>
        public void Cooldown(double readyAtMs, double totalMs) { _readyAtMs = readyAtMs; _totalMs = totalMs; }

        public double ReadyAtMs { get { return _readyAtMs; } }
        public double TotalMs { get { return _totalMs; } }
    }
}
