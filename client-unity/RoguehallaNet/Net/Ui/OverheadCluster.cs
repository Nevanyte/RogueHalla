using System.Collections.Generic;

namespace Roguehalla.Net.Ui
{
    /// <summary>Turns an overhead stack into the parts of a box; holds the elite star's three numbers.</summary>
    public static class OverheadCluster
    {
        public const float EliteStarSize = .3f, EliteStarPlate = 1.4f, EliteStarRise = .02f;

        /// <summary>Adds the parts of a stack to the latest box of the layout, in stack order; returns how many.</summary>
        public static int AddParts(OverheadBoxLayout layout, List<OverheadElement> stack, bool eliteStar,
            float worldPerLocal, float nameWidth, float nameHeight)
        {
            int nameAt = -1;
            for (int i = 0; i < stack.Count; i++) if (stack[i].Kind == OverheadKind.Name) { nameAt = i; break; }
            if (nameAt < 0) return 0;
            float k = worldPerLocal * OverheadStack.UnitsPerPx;
            var name = stack[nameAt];
            float nameC = name.Y + name.H / 2f, grow = .025f * worldPerLocal;
            int added = 0;
            for (int i = 0; i < stack.Count; i++)
            {
                var e = stack[i];
                switch (e.Kind)
                {
                    case OverheadKind.Name:
                        if (i != nameAt) break;
                        layout.AddPart(-nameWidth / 2f, -nameHeight / 2f, nameWidth / 2f, nameHeight / 2f); added++;
                        if (eliteStar)
                        {
                            float half = EliteStarSize * EliteStarPlate * worldPerLocal / 2f;
                            float cy = k * (name.Y + name.H - nameC) + worldPerLocal * (EliteStarRise + EliteStarSize / 2f);
                            layout.AddPart(-half, cy - half, half, cy + half); added++;
                        }
                        break;
                    case OverheadKind.WishIcon:
                    case OverheadKind.MarkPips:
                    case OverheadKind.BountyNumber:
                        layout.AddPart(k * (e.X - e.W / 2f), k * (e.Y - nameC), k * (e.X + e.W / 2f), k * (e.Y + e.H - nameC)); added++;
                        break;
                    case OverheadKind.SecondaryWeapon:
                    case OverheadKind.StatMark:
                        layout.AddPart(k * (e.X - e.W / 2f) - grow, k * (e.Y - nameC) - grow, k * (e.X + e.W / 2f) + grow, k * (e.Y + e.H - nameC) + grow); added++;
                        break;
                    case OverheadKind.BountyCrown:
                        {
                            float side = .9f * e.W * k, cx = k * e.X, cy = k * (e.Y + e.H / 2f - nameC);
                            layout.AddPart(cx - side / 2f, cy - side / 2f, cx + side / 2f, cy + side / 2f); added++;
                        }
                        break;
                }
            }
            return added;
        }
    }
}
