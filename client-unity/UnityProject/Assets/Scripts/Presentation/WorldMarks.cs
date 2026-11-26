// World markers for delivered quest givers and merchants, as legacy QuestView / TraderView: a bright "!" over a giver with an offer the
// player has not accepted, a dim "?" over one they hold, "Turn in here" when the quest is ready, and a diamond with the merchant's name.
// Pooled sprites and text; a marker is drawn only for a delivered row that is not in fog. Presentation only.
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;

namespace Roguehalla.Client
{
    /// <summary>Draws the world marks the server reports (such as merchants): glyph and label plates, with a catalog figure for traders.</summary>
    public sealed class WorldMarks
    {
        sealed class Mark { public SpriteRenderer Glyph, GlyphPlate, LabelPlate, Figure, Shadow, Cast, Goods; public Transform FigureRoot; public TextMeshPro Label; public string Text; }
        readonly Transform _root;
        readonly Sprite _apothecary, _barterMerchant, _tradeBarrel;
        readonly List<Mark> _marks = new List<Mark>();
        int _used;
        public int Used { get { return _used; } }
        const int Order = 29000;

        /// <summary>The look catalog the merchants' figures are drawn from (null: the neutral silhouette, as before).</summary>
        public WorldMarks(Transform root, ShowcaseCatalog catalog = null)
        {
            _root = root;
            if (catalog == null) return;
            _apothecary = Resources.Load<Sprite>("StormInk/Actors/Apothecary");
            _barterMerchant = Resources.Load<Sprite>("StormInk/Actors/BarterMerchant");
            _tradeBarrel = Resources.Load<Sprite>("StormInk/Props/TradeBarrel");
        }

        /// <summary>Same scale as every observed actor (SpriteActorPresenter.ActorVisualScale), so a trader is as tall as a fighter drawn from the same look.</summary>
        const float FigureScale = 1.2f;

        /// <param name="canSee">True when the ground at a sim position is not fogged; null shows everything (no fog yet).</param>
        public void Update(CollectionsView c, System.Func<float, float, bool> canSee) { Update(c, canSee, Time.unscaledDeltaTime); }

        /// <summary>Answers the local fighter's overhead cluster; the text labels fade while they lie on it (null: nothing hides).</summary>
        public LocalClusterSource LocalCluster { get; set; }

        LabelClearanceBook _book = new LabelClearanceBook();
        ClusterHold _hold;
        QuestNameOnce _names = new QuestNameOnce();
        bool _run, _haveCluster, _reduced;
        LabelBox _cluster;
        float _dt;

        /// <summary>One frame: <paramref name="dt"/> is the unscaled seconds since the last one (the clearance fades step by it).</summary>
        public void Update(CollectionsView c, System.Func<float, float, bool> canSee, float dt)
        {
            _used = 0;
            _book.Begin(); _names.Begin();
            _run = !WorldLabelVisibility.Hidden && _root.gameObject.activeInHierarchy;
            _dt = dt; _haveCluster = false;
            if (_run)
            {
                _reduced = UiMotion.Reduced;
                bool answered = false; LabelBox box = default;
                if (LocalCluster != null && LocalCluster(out var r)) { answered = true; box = new LabelBox(r.xMin, r.yMin, r.xMax, r.yMax); }
                _haveCluster = _hold.Read(answered, in box, out _cluster);
            }
            if (c != null)
            {
                var theme = UiTheme.Current;
                var quests = c.Quests;
                if (quests?.Available == true)
                {
                    var rows = quests.Rows;
                    for (int qi = 0; qi < rows.Count; qi++)
                    {
                        var q = rows[qi];
                        if (q.State != QuestRowState.Open || !QuestPresentation.ShowsOffer(q)) continue;
                        float x = (float)q.GiverX, y = (float)q.GiverY;
                        if (canSee != null && !canSee(x, y)) continue;
                        bool accepted = q.OwnerAccepted;
                        var m = Next(); HideFigure(m);
                        Place(m, ArenaView.WorldPoint(x, y) + new Vector3(0, 2.55f, 0), IndicatorArt.Get(Indicators.QuestMark(accepted) == '!' ? "bang" : "query"), (Color)theme.Accent, accepted ? .6f : 1f, .5f);
                        bool turnIn = accepted && q.OwnerReady && !HasRemoteTurnIn(quests, q);
                        string label = !accepted && !q.Carrier.HasValue && QuestPresentation.IsBoardRow(q) && !string.IsNullOrEmpty(q.Name)
                            ? q.Name : q.GiverLabel;
                        SetLabel(m, turnIn ? "Turn in here" : label, turnIn ? theme.Accent : theme.TextPrimary, new Vector3(0, -.2f, 0), 2.3f, 1, x, y);
                    }
                    var points = quests.Objectives;
                    for (int oi = 0; oi < points.Count; oi++)
                    {
                        var objective = points[oi];
                        if (DrawnObjective(quests, objective, canSee, out _)) _names.Offer(objective.Quest.Value, (float)objective.X, (float)objective.Y);
                    }
                    for (int oi = 0; oi < points.Count; oi++)
                    {
                        var objective = points[oi];
                        if (!DrawnObjective(quests, objective, canSee, out var row)) continue;
                        bool publicMark = row.Carrier.HasValue && (!row.OwnerAccepted || row.OwnerCompleted);
                        float ox = (float)objective.X, oy = (float)objective.Y;
                        bool turnIn = row.OwnerAccepted && row.OwnerReady && !row.OwnerCompleted;
                        if (turnIn && objective.X == row.GiverX && objective.Y == row.GiverY) continue;
                        string label = turnIn ? "Turn in here" : _names.Carries(objective.Quest.Value, ox, oy) ? row.Name : null;
                        ObjectivePoint(ox, oy, label, publicMark ? "dashring" : "diamond", canSee);
                    }
                    var drops = quests.Drops;
                    for (int di = 0; di < drops.Count; di++)
                        ObjectivePoint((float)drops[di].X, (float)drops[di].Y, QuestPresentation.RewardName(drops[di].Reward), "crate", canSee);
                }
                if (c.Trade != null && c.Trade.Available)
                {
                    var merchants = c.Trade.Merchants;
                    for (int ti = 0; ti < merchants.Count; ti++)
                    {
                        var t = merchants[ti];
                        float x = (float)t.X, y = (float)t.Y;
                        if (canSee != null && !canSee(x, y)) continue;
                        var m = Next();
                        Color tone = (Color)(t.Kind == TradeKind.Stats ? theme.Info : theme.Accent);
                        // The trader stands there: the marker needs a body to point at (lab finding). Figure first: its top decides where the marker and the name sit.
                        float top = Figure(m, t.Kind, ArenaView.WorldPoint(x, y), x / 32f, y / 32f, 2000 + Mathf.RoundToInt(y * .5f), tone);
                        Place(m, ArenaView.WorldPoint(x, y) + new Vector3(0, top + .62f, 0), IndicatorArt.Get("diamond"), tone, 1f, .46f);
                        SetLabel(m, t.Name, theme.TextPrimary, new Vector3(0, -.2f, 0), 2.3f, 3 + (int)t.Kind, 0f, 0f);
                    }
                }
            }
            for (int i = _used; i < _marks.Count; i++) { HideFigure(_marks[i]); _marks[i].Glyph.enabled = false; _marks[i].GlyphPlate.enabled = false; _marks[i].LabelPlate.enabled = false; _marks[i].Label.gameObject.SetActive(false); }
        }

        static bool HasRemoteTurnIn(QuestView view, QuestRowView row)
        {
            if (!QuestPresentation.IsBoardRow(row)) return false;
            var points = view.Objectives;
            for (int i = 0; i < points.Count; i++)
                if (points[i].Quest.Equals(row.Handle))
                    return points[i].X != row.GiverX || points[i].Y != row.GiverY;
            return false;
        }

        /// <summary>True when the objective point is drawn: its row is a board row, Open, public or the player's own accepted one, and the point is not fogged. The row is found by an indexed loop.</summary>
        static bool DrawnObjective(QuestView quests, QuestObjectiveView objective, System.Func<float, float, bool> canSee, out QuestRowView row)
        {
            row = null;
            var rows = quests.Rows;
            for (int i = 0; i < rows.Count; i++) if (rows[i].Handle.Equals(objective.Quest)) { row = rows[i]; break; }
            if (row == null || !QuestPresentation.IsBoardRow(row) || row.State != QuestRowState.Open) return false;
            bool publicMark = row.Carrier.HasValue && (!row.OwnerAccepted || row.OwnerCompleted);
            if (!publicMark && (!row.OwnerAccepted || row.OwnerCompleted)) return false;
            return canSee == null || canSee((float)objective.X, (float)objective.Y);
        }

        void ObjectivePoint(float x, float y, string label, string glyph, System.Func<float, float, bool> canSee)
        {
            if (canSee != null && !canSee(x, y)) return;
            var mark = Next(); HideFigure(mark);
            var theme = UiTheme.Current;
            Place(mark, ArenaView.WorldPoint(x, y) + new Vector3(0, .9f, 0), IndicatorArt.Get(glyph), theme.Accent, 1f, .42f);
            SetLabel(mark, label, theme.TextPrimary, new Vector3(0, -.12f, 0), 2f, 2, x, y);
        }

        Mark Next()
        {
            if (_used == _marks.Count)
            {
                var m = new Mark();
                // Opaque dark plates behind the glyph and the label keep both above 4.5:1 on any ground (sand, snow): plates, not outlines.
                m.GlyphPlate = ShowcaseSprites.Create(_root, "World marker plate", "bar", Order - 2, Color.white, Vector2.one, Vector3.zero, false);
                m.LabelPlate = ShowcaseSprites.Create(_root, "World marker label plate", "bar", Order - 1, Color.white, Vector2.one, Vector3.zero, false);
                m.GlyphPlate.enabled = m.LabelPlate.enabled = false;
                m.Glyph = ShowcaseSprites.Create(_root, "World marker", "bar", Order, Color.white, Vector2.one, Vector3.zero, false);
                // Same parts and scale as an observed actor (SpriteActorPresenter.Create): contact and cast shadow, the authored body lit by the field, drawn under one 1.2x root.
                var fr = new GameObject("Trader"); fr.transform.SetParent(_root, false); fr.transform.localScale = new Vector3(FigureScale, FigureScale, 1f); m.FigureRoot = fr.transform;
                m.Shadow = ShowcaseSprites.Create(m.FigureRoot, "Trader contact shadow", "actorshadow", 1990, Color.white, new Vector2(1.2f, .45f), new Vector3(.13f, -.04f), false); m.Shadow.enabled = false;
                m.Cast = ShowcaseSprites.Create(m.FigureRoot, "Trader cast shadow", "actorshadow", 1989, Color.white, new Vector2(1.2f, .45f), new Vector3(.13f, -.04f), false); m.Cast.enabled = false;
                m.Goods = ShowcaseSprites.Create(m.FigureRoot, "Trader goods", "bar", 2000, Color.white, Vector2.one, Vector3.zero); m.Goods.enabled = false;
                m.Figure = ShowcaseSprites.Create(m.FigureRoot, "Trader figure", "bar", 2000, Color.white, Vector2.one, Vector3.zero); m.Figure.enabled = false;
                m.Figure.sharedMaterial = ShowcaseSprites.ActorLit;
                var go = new GameObject("World marker label"); go.transform.SetParent(_root, false);
                m.Label = go.AddComponent<TextMeshPro>();
                var font = Resources.Load<TMP_FontAsset>("Showcase/NunitoBold");
                m.Label.font = font != null ? font : TMP_Settings.defaultFontAsset;
                m.Label.richText = false; m.Label.alignment = TextAlignmentOptions.Center; m.Label.enableWordWrapping = false;
                m.Label.fontSharedMaterial = OverheadIndicators.OutlinedMaterial(m.Label.font); m.Label.rectTransform.sizeDelta = new Vector2(5f, .5f);
                m.Label.GetComponent<MeshRenderer>().sortingOrder = Order + 1; go.SetActive(false);
                _marks.Add(m);
            }
            return _marks[_used++];
        }

        static void HideFigure(Mark m)
        {
            m.Figure.enabled = m.Shadow.enabled = m.Cast.enabled = m.Goods.enabled = false;
        }

        /// <summary>
        /// Draws the merchant and their stock at the delivered position and returns the figure's height for the label.
        /// </summary>
        float Figure(Mark m, TradeKind kind, Vector3 feet, float tileX, float tileY, int order, Color tone)
        {
            m.FigureRoot.position = feet;
            var sprite = kind == TradeKind.Stats ? _apothecary : _barterMerchant;
            m.Figure.enabled = true; m.Figure.sortingOrder = order;
            m.Shadow.enabled = true; m.Shadow.sortingOrder = order - 8; m.Cast.sortingOrder = order - 9;
            AtmosphereShadows.ApplyActor(m.Shadow, tileX, tileY, 0f, 0f, m.Cast);
            if (sprite == null)
            {
                m.Figure.sprite = ShowcaseIdentityAssets.GetFactionSilhouette(-1); m.Figure.color = Color.Lerp(Color.white, tone, .5f);
                m.Figure.transform.localScale = new Vector3(.8f, 1.35f, 1f); m.Figure.transform.localPosition = new Vector3(0, .55f, 0);
                m.Goods.enabled = false;
                return (.55f + .675f) * FigureScale;
            }
            const float height = 1.5f;
            m.Figure.sprite = sprite; m.Figure.color = Color.white;
            m.Figure.transform.localScale = Vector3.one * height; m.Figure.transform.localPosition = new Vector3(0, .08f, 0);
            float top = .08f + sprite.bounds.max.y * height;
            var barrel = _tradeBarrel;
            if (barrel != null && barrel.bounds.size.y > 1e-4f)
            {
                m.Goods.enabled = true; m.Goods.sprite = barrel; m.Goods.sortingOrder = order + 1;
                float s = GoodsHeight / barrel.bounds.size.y;       // the barrel reads as a stall's goods, about knee to hip high beside the figure
                m.Goods.transform.localScale = new Vector3(s, s, 1f);
                m.Goods.transform.localPosition = new Vector3(GoodsSide, .02f - barrel.bounds.min.y * s, 0f);
                m.Goods.color = Color.white;
            }
            else m.Goods.enabled = false;
            return top * FigureScale;
        }
        const float GoodsHeight = .62f, GoodsSide = .62f;

        static void Place(Mark m, Vector3 at, Sprite sprite, Color color, float alpha, float size)
        {
            m.Glyph.enabled = true; m.Glyph.sprite = sprite; color.a = alpha; m.Glyph.color = color;
            m.Glyph.transform.position = at; m.Glyph.transform.localScale = new Vector3(size, size, 1);
            var plate = (Color)UiTheme.Current.OutlineDark; plate.a = 1f;
            m.GlyphPlate.enabled = true; m.GlyphPlate.color = plate; m.GlyphPlate.transform.position = at; m.GlyphPlate.transform.localScale = new Vector3(size * 1.32f, size * 1.32f, 1);
        }

        void SetLabel(Mark m, string text, Color32 color, Vector3 offset, float size, int kind, float id0, float id1)
        {
            float alpha = 1f;
            if (string.IsNullOrEmpty(text)) { m.Label.gameObject.SetActive(false); m.LabelPlate.enabled = false; return; }
            if (!ReferenceEquals(m.Text, text)) { m.Text = text; m.Label.text = text; }
            m.Label.fontSize = size; var c = (Color)color; c.a = alpha; m.Label.color = c;
            m.Label.transform.position = m.Glyph.transform.position + offset - new Vector3(0, .25f, 0);
            m.Label.gameObject.SetActive(true);
            var lp = (Color)UiTheme.Current.OutlineDark; lp.a = 1f;
            m.LabelPlate.enabled = true; m.LabelPlate.color = lp;
            m.LabelPlate.transform.position = m.Label.transform.position + new Vector3(0, .02f, 0);
            float width = m.Label.preferredWidth + .2f, height = size * .12f + .02f;
            m.LabelPlate.transform.localScale = new Vector3(width, height, 1);
            var at = m.LabelPlate.transform.position;
            var box = LabelBox.Centred(at.x, at.y, width, height);
            alpha = _run ? _book.Next(kind, id0, id1, in box, _haveCluster, in _cluster, _dt, _reduced) : _book.Keep(kind, id0, id1);
            if (alpha < 1f) { c.a *= alpha; m.Label.color = c; lp.a *= alpha; m.LabelPlate.color = lp; }
        }
    }
}
