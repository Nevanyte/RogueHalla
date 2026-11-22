using System.Collections.Generic;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Cached, presentation-only Coliseum gates. Each delivered row is painted once and only its visibility changes.</summary>
    public sealed class ColiseumGatePresenter : System.IDisposable
    {
        readonly List<GameObject> _gateRoots = new List<GameObject>();
        bool _locked, _hasState;
        ColiseumGateWalls _fallback;

        public static ColiseumGatePresenter Build(Transform parent, MapBlob map, Material wallMaterial = null)
        {
            var presenter = new ColiseumGatePresenter();
            presenter.Create(parent, map, wallMaterial);
            return presenter;
        }

        void Create(Transform parent, MapBlob map, Material wallMaterial)
        {
            var gates = ColiseumGateLayout.DeliveredGates(map);
            if (wallMaterial != null && map != null)
            {
                var painted = new HashSet<int>();
                foreach (var gate in gates)
                    foreach (var tile in gate.Tiles) painted.Add(tile.Y * map.W + tile.X);
                _fallback = ColiseumGateWalls.Build(parent, map, wallMaterial, painted);
            }
            if (gates.Count == 0) return;
            var root = new GameObject("Coliseum gate artwork"); root.transform.SetParent(parent, false);
            var editable = Resources.Load<ColiseumGateArt>("StormInk/Props/ColiseumGatePierArt");
            float barWidth = editable != null ? Mathf.Clamp(editable.GateBarWidthWorldUnits, .02f, .2f) : .055f;
            float barHeight = editable != null ? Mathf.Clamp(editable.GateBarHeightWorldUnits, .3f, 2.5f) : 1.18f;
            Color iron = editable != null ? editable.GateIron : new Color(.10f, .12f, .15f, 1f);
            Color highlight = editable != null ? editable.GateIronHighlight : new Color(.48f, .52f, .55f, 1f);
            float frameDepth = editable != null ? Mathf.Clamp(editable.FrameDepthWorldUnits, .12f, .44f) : .31f;
            var plate = editable != null ? editable.RailIronwork : null;
            foreach (var gate in gates)
            {
                var gateRoot = new GameObject(gate.Side == 0 ? "North portcullis" : gate.Side == 1 ? "East portcullis" : gate.Side == 2 ? "South portcullis" : "West portcullis");
                gateRoot.transform.SetParent(root.transform, false); gateRoot.SetActive(false); _gateRoots.Add(gateRoot);
                foreach (var tile in gate.Tiles) AddTile(gateRoot.transform, tile, gate, barWidth, barHeight, frameDepth, iron, highlight, plate);
            }
        }

        static void AddTile(Transform parent, MapTile tile, ColiseumGateLayout.Gate gate, float barWidth, float barHeight, float frameDepth, Color iron, Color highlight, Sprite plate)
        {
            int row = 2000 + (tile.Y + 1) * 16;
            Segment(tile, gate, out var from, out var to);
            // Every sill/rail follows its delivered tile segment. East/west openings therefore
            // recede along the camera's compressed map-Y axis instead of becoming horizontal rungs.
            CreateBar(parent, "Portcullis sill", from, to, .055f, .035f, iron, row);

            for (int k = 0; k < 3; k++)
            {
                float along = (k + .5f) / 3f;
                float x = gate.RunsAlongScreen ? tile.X + along : tile.X + .5f;
                float y = gate.RunsAlongScreen ? tile.Y + 1f : tile.Y + along;
                Vector3 basePoint = ArenaView.WorldPoint(x * 32f, y * 32f) + new Vector3(0, .06f, 0);
                ShowcaseSprites.Create(parent, "Portcullis upright", "bar", row + 1,
                    iron, new Vector2(barWidth, barHeight), basePoint + new Vector3(0, barHeight * .5f, 0), true);
                ShowcaseSprites.Create(parent, "Portcullis ink edge", "bar", row + 2,
                    highlight, new Vector2(Mathf.Max(.018f, barWidth * .18f), barHeight * .94f),
                    basePoint + new Vector3(-barWidth * .22f, barHeight * .5f, -.001f), true);
            }

            for (int level = 0; level < 2; level++)
            {
                float lift = barHeight * (level == 0 ? .34f : .76f);
                CreateBar(parent, "Portcullis cross rail", from, to, .07f, lift, iron, row + 3, gate.RunsAlongScreen ? plate : null);
                CreateBar(parent, "Portcullis rail edge", from, to, .018f, lift + .026f, highlight, row + 4);
            }

            if (!gate.RunsAlongScreen)
                AddSideFrame(parent, from, to, row, frameDepth, barHeight, gate.Side == 1 ? 1f : -1f, iron, highlight, plate);
        }

        // The bars overlap in an end-on view. Draw the actual thickness of the iron top rail,
        // the near edge and its wraparound straps within the closed tile's footprint.
        static void AddSideFrame(Transform parent, Vector3 from, Vector3 to, int row,
            float depth, float height, float outward, Color iron, Color edge, Sprite plate)
        {
            float top = height + .04f;
            var cheek = Vector3.right * (outward * depth * .36f);
            CreateBar(parent, "Portcullis cap", from, to, depth, top, Color.Lerp(iron, edge, .44f), row + 5, plate);
            CreateBar(parent, "Portcullis outer cheek", from + cheek, to + cheek, depth * .26f,
                top - .05f, iron, row + 6);
            // One strap binds the end of each tile-length section. Its painted plate supplies
            // the grain and rivet heads, so the hardware reads even against a bright floor.
            Vector3 joint = Vector3.Lerp(from, to, .87f);
            var half = Vector3.right * depth * .55f;
            CreateBar(parent, "Portcullis iron strap", joint - half, joint + half,
                .075f, top, iron, row + 7, plate);
        }

        static void Segment(MapTile tile, ColiseumGateLayout.Gate gate, out Vector3 from, out Vector3 to)
        {
            if (gate.RunsAlongScreen)
            {
                from = ArenaView.WorldPoint(tile.X * 32f, (tile.Y + 1f) * 32f);
                to = ArenaView.WorldPoint((tile.X + 1f) * 32f, (tile.Y + 1f) * 32f);
            }
            else
            {
                from = ArenaView.WorldPoint((tile.X + .5f) * 32f, tile.Y * 32f);
                to = ArenaView.WorldPoint((tile.X + .5f) * 32f, (tile.Y + 1f) * 32f);
            }
        }

        static SpriteRenderer CreateBar(Transform parent, string name, Vector3 from, Vector3 to, float thickness,
            float lift, Color color, int order, Sprite plate = null)
        {
            Vector3 delta = to - from;
            var renderer = ShowcaseSprites.Create(parent, name, "bar", order, color,
                new Vector2(Mathf.Max(.01f, delta.magnitude), thickness), (from + to) * .5f + new Vector3(0, lift, 0), true);
            if (plate != null)
            {
                renderer.sprite = plate;
                renderer.color = Color.white;
                Vector2 size = plate.bounds.size;
                renderer.transform.localScale = new Vector3(delta.magnitude / size.x, thickness / size.y, 1f);
            }
            renderer.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            return renderer;
        }

        /// <summary>Changes cached visibility only when the server-observed gate state changes.</summary>
        public void SetLocked(bool locked)
        {
            if (_hasState && _locked == locked) return;
            _hasState = true; _locked = locked;
            _fallback?.SetLocked(locked);
            foreach (var gate in _gateRoots) if (gate != null && gate.activeSelf != locked) gate.SetActive(locked);
        }

        public void Dispose()
        {
            foreach (var gate in _gateRoots) if (gate != null) RegionUtil.Kill(gate);
            _gateRoots.Clear();
            _fallback?.Dispose();
        }
    }
}
