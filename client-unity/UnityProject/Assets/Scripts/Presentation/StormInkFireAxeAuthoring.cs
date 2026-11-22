using System;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Directional Fire axe performance columns, including the independent contact drawings for releases A and B.</summary>
    public enum StormInkFireAxePose { Windup, ReleaseA, Recovery, Guard, Stride, ReleaseB, WindupB, RecoveryB }

    /// <summary>Painted Fire animation registration. Rows are S, SE, E, NE, N, NW, W and SW.</summary>
    [CreateAssetMenu(menuName = "RogueHalla/Storm Ink/Fire axe authoring")]
    public sealed class StormInkFireAxeAuthoring : ScriptableObject
    {
        const int Rows = 8;
        [Serializable] struct Palms
        {
            public Vector2Int Right, Left;
        }

        [Serializable] struct DirectionalCycle
        {
            [Range(0, Rows - 1)] public int Row;
            public int[] GuardColumns, StrideColumns;
            public float GuardAxeAngleDelta;
        }

        [SerializeField] Texture2D atlas;
        [SerializeField, Min(6)] int columns = 6;
        [SerializeField, Min(.1f)] float worldHeight = 1.15f;
        [SerializeField] Vector2 footPivot = new Vector2(.5f, 5f / 128f);
        [SerializeField, Min(.1f)] float idleFramesPerSecond = 4f;
        [SerializeField] int[] guardColumns = { 3 };
        [SerializeField] int[] strideColumns = { 4 };
        [SerializeField] DirectionalCycle[] directionalCycles = Array.Empty<DirectionalCycle>();
        [SerializeField] Palms[] palms = Array.Empty<Palms>();

        static StormInkFireAxeAuthoring _default;
        Sprite[] _frames;

        public float WorldHeight => worldHeight;
        public Vector2 FootPivot => footPivot;
        public int GuardStages => guardColumns.Length;
        public int StrideStages => strideColumns.Length;
        public static StormInkFireAxeAuthoring Load() => _default != null ? _default :
            _default = Resources.Load<StormInkFireAxeAuthoring>("StormInk/Actors/FireAxeAuthoring");

        public int GuardStagesFor(int row) => Cycle(row, false).Length;
        public int StrideStagesFor(int row) => Cycle(row, true).Length;

        public int IdleStage(float elapsed) => IdleStage(-1, elapsed);

        public int IdleStage(int row, float elapsed)
        {
            int stages = GuardStagesFor(row);
            return stages > 0 ? Mathf.FloorToInt(Mathf.Max(0f, elapsed) * idleFramesPerSecond) % stages : 0;
        }

        public float GuardAxeAngle(float angle, int row)
        {
            foreach (var cycle in directionalCycles)
                if (cycle.Row == row) return angle + cycle.GuardAxeAngleDelta;
            return angle;
        }

        int[] Cycle(int row, bool walking)
        {
            foreach (var cycle in directionalCycles)
            {
                if (cycle.Row != row) continue;
                var sequence = walking ? cycle.StrideColumns : cycle.GuardColumns;
                if (sequence != null && sequence.Length > 0) return sequence;
            }
            return walking ? strideColumns : guardColumns;
        }

        int CycleColumn(int row, int stage, bool walking, int fallback)
        {
            var sequence = Cycle(row, walking);
            return sequence.Length == 0 ? fallback : sequence[Mathf.Clamp(stage, 0, sequence.Length - 1)];
        }

        public Sprite Frame(int row, StormInkFireAxePose pose, int stage = 0)
        {
            if ((uint)row >= Rows) return null;
            EnsureFrames();
            if (_frames == null) return null;
            int index = pose switch
            {
                StormInkFireAxePose.Guard => CycleColumn(row, stage, false, 3),
                StormInkFireAxePose.Stride => CycleColumn(row, stage, true, 4),
                _ => (int)pose
            };
            return (uint)index < columns ? _frames[row * columns + index] : null;
        }

        public bool TryHand(Sprite body, bool left, out Vector2 uv)
        {
            uv = default;
            if (_frames == null || body == null || body.texture != atlas) return false;
            var rect = body.rect;
            int column = Mathf.RoundToInt(rect.x / rect.width);
            int row = Rows - 1 - Mathf.RoundToInt(rect.y / rect.height);
            if ((uint)row >= Rows || (uint)column >= columns) return false;
            int index = row * columns + column;
            if (_frames[index] != body || index >= palms.Length) return false;
            var point = left ? palms[index].Left : palms[index].Right;
            uv = new Vector2((point.x + .5f) / rect.width, 1f - (point.y + .5f) / rect.height);
            return true;
        }

        void EnsureFrames()
        {
            if (_frames != null || atlas == null || columns < 6 ||
                atlas.width * Rows != atlas.height * columns || palms.Length != Rows * columns) return;
            int side = atlas.width / columns;
            _frames = new Sprite[Rows * columns];
            for (int row = 0; row < Rows; row++)
            for (int col = 0; col < columns; col++)
            {
                var rect = new Rect(col * side, (Rows - 1 - row) * side, side, side);
                var frame = Sprite.Create(atlas, rect, footPivot, side * .89f, 0, SpriteMeshType.FullRect);
                frame.name = $"FireAxe/{row}/{col}";
                frame.hideFlags = HideFlags.HideAndDontSave;
                _frames[row * columns + col] = frame;
            }
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            InvalidateFrames();
            StormInkFireSwordAuthoring.SharedFireRigChanged();
        }
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
