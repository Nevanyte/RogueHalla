using System;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Editable Fire bow draw and release drawings. Unready bearings retain the existing Fire body artwork.</summary>
    [CreateAssetMenu(menuName = "RogueHalla/Storm Ink/Fire bow body authoring")]
    public sealed class StormInkFireBowAuthoring : ScriptableObject
    {
        const int Rows = 8, Columns = 10;

        [Serializable]
        struct Palms
        {
            public Vector2Int Right, Left;
        }

        [SerializeField] Texture2D atlas;
        [SerializeField] bool[] readyRows = new bool[Rows];
        [SerializeField] Palms[] palms = Array.Empty<Palms>();
        [SerializeField, Range(.05f, .8f)] float midDrawAt = .22f;
        [SerializeField, Range(.2f, .95f)] float sustainedDrawAt = .68f;

        static StormInkFireBowAuthoring _default;
        Sprite[] _frames;
        Vector2 _framePivot;

        public static StormInkFireBowAuthoring Load() => _default != null ? _default :
            _default = Resources.Load<StormInkFireBowAuthoring>("StormInk/Actors/FireBowAuthoring");

        public float WorldHeight => StormInkFireAxeAuthoring.Load()?.WorldHeight ?? 1.15f;

        public bool IsReady(int row) => (uint)row < Rows && readyRows != null && readyRows.Length == Rows && readyRows[row];

        internal static int PoseColumn(StormInkWeaponMotion.Phase phase, float progress, bool alternate,
            float middle = .22f, float sustained = .68f)
        {
            int release = alternate ? 5 : 0;
            middle = Mathf.Clamp(middle, .05f, .8f);
            sustained = Mathf.Clamp(sustained, middle, .95f);
            switch (phase)
            {
                case StormInkWeaponMotion.Phase.Ready:
                    progress = Mathf.Clamp01(progress);
                    return release + (progress < middle ? 0 : progress < sustained ? 1 : 2);
                case StormInkWeaponMotion.Phase.Strike: return release + 3;
                case StormInkWeaponMotion.Phase.Follow: return release + 4;
                default: return -1;
            }
        }

        internal int PoseColumn(StormInkWeaponMotion.Phase phase, float progress, bool alternate) =>
            PoseColumn(phase, progress, alternate, midDrawAt, sustainedDrawAt);

#if UNITY_EDITOR
        public void PreviewParameters(int column, out StormInkFireSwordMotionPhase phase, out float progress)
        {
            if ((uint)column >= Columns) throw new ArgumentOutOfRangeException(nameof(column));
            int pose = column % 5;
            float middle = Mathf.Clamp(midDrawAt, .05f, .8f);
            float sustained = Mathf.Clamp(sustainedDrawAt, middle, .95f);
            switch (pose)
            {
                case 0: phase = StormInkFireSwordMotionPhase.Windup; progress = middle * .5f; break;
                case 1: phase = StormInkFireSwordMotionPhase.Windup; progress = (middle + sustained) * .5f; break;
                case 2: phase = StormInkFireSwordMotionPhase.Windup; progress = (sustained + 1f) * .5f; break;
                case 3: phase = StormInkFireSwordMotionPhase.Strike; progress = .5f; break;
                default: phase = StormInkFireSwordMotionPhase.Follow; progress = .85f; break;
            }
        }
#endif

        public Sprite Frame(int row, int column)
        {
            if (!IsReady(row) || (uint)column >= Columns) return null;
            EnsureFrames();
            return _frames == null ? null : _frames[row * Columns + column];
        }

        internal Sprite Frame(int row, StormInkWeaponMotion.Phase phase, float progress, bool alternate)
        {
            int column = PoseColumn(phase, progress, alternate);
            return column >= 0 ? Frame(row, column) : null;
        }

        public bool TryHand(Sprite body, bool left, out Vector2 uv)
        {
            uv = default;
            if (body == null || atlas == null || body.texture != atlas || palms == null || palms.Length != Rows * Columns) return false;
            EnsureFrames();
            if (_frames == null) return false;
            int row = PoseRow(body), column = Mathf.RoundToInt(body.rect.x / body.rect.width);
            if (!IsReady(row) || (uint)column >= Columns) return false;
            int index = row * Columns + column;
            if (_frames[index] != body) return false;
            var point = left ? palms[index].Left : palms[index].Right;
            int side = Mathf.RoundToInt(body.rect.width);
            uv = new Vector2((point.x + .5f) / side, 1f - (point.y + .5f) / side);
            return true;
        }

        static int PoseRow(Sprite body) => Rows - 1 - Mathf.RoundToInt(body.rect.y / body.rect.height);

        void EnsureFrames()
        {
            var rig = StormInkFireAxeAuthoring.Load();
            Vector2 pivot = rig != null ? rig.FootPivot : new Vector2(.5f, 5f / 128f);
            if (_frames != null && _framePivot != pivot) InvalidateFrames();
            if (_frames != null || atlas == null || atlas.width % Columns != 0 || atlas.height % Rows != 0 ||
                atlas.width / Columns != atlas.height / Rows || palms == null || palms.Length != Rows * Columns) return;
            int side = atlas.width / Columns;
            _frames = new Sprite[Rows * Columns];
            _framePivot = pivot;
            for (int row = 0; row < Rows; row++)
            for (int column = 0; column < Columns; column++)
            {
                var rect = new Rect(column * side, (Rows - 1 - row) * side, side, side);
                var frame = Sprite.Create(atlas, rect, pivot, side * .89f, 0, SpriteMeshType.FullRect);
                frame.name = $"FireBow/{row}/{column}";
                frame.hideFlags = HideFlags.HideAndDontSave;
                _frames[row * Columns + column] = frame;
            }
        }

#if UNITY_EDITOR
        void OnValidate() => InvalidateFrames();
#endif

        void InvalidateFrames()
        {
            if (_frames == null) return;
            var old = _frames;
            _frames = null;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () =>
            {
                foreach (var frame in old) if (frame != null) DestroyImmediate(frame);
            };
#endif
        }
    }
}
