using System;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Pose columns for two five-phase Fire sword releases.</summary>
    public enum StormInkFireSwordPose
    {
        AnticipationA, PassingA, ReleaseA, FollowThroughA, RecoveryA,
        AnticipationB, PassingB, ReleaseB, FollowThroughB, RecoveryB
    }

    /// <summary>Public phase vocabulary for inspecting Fire sword artwork without exposing the presenter's clock type.</summary>
    public enum StormInkFireSwordMotionPhase { Windup, Strike, Follow }

    /// <summary>Artist-authored empty-handed Fire sword performances and anatomical sockets.</summary>
    [CreateAssetMenu(menuName = "RogueHalla/Storm Ink/Fire sword authoring")]
    public sealed class StormInkFireSwordAuthoring : ScriptableObject
    {
        const int Rows = 8;
        const int Columns = 10;

        [Serializable]
        struct Palms
        {
            public Vector2Int Right;
            public Vector2Int Left;
        }

        [Serializable]
        struct PosePresentation
        {
            [Range(0, Rows - 1)] public int Row;
            [Range(0, Columns - 1)] public int Column;
            public float WeaponAngleOffset;
            public int WeaponDepthOffset;
        }

        [Serializable]
        struct ReleaseTiming
        {
            [Range(.1f, .9f)] public float PassingAt;
            [Range(.1f, .9f)] public float RecoveryAt;
        }

        [SerializeField] Texture2D atlas;
        [SerializeField] PosePresentation[] poses = Array.Empty<PosePresentation>();
        [SerializeField] Palms[] palms = Array.Empty<Palms>();
        [SerializeField] ReleaseTiming releaseATiming = new ReleaseTiming { PassingAt = .9f, RecoveryAt = .72f };
        [SerializeField] ReleaseTiming releaseBTiming = new ReleaseTiming { PassingAt = .9f, RecoveryAt = .72f };

        static StormInkFireSwordAuthoring _default;
        Sprite[] _frames;
        float[] _angleOffsets;
        int[] _depthOffsets;
        bool _poseOffsetsChecked;

        public float WorldHeight => StormInkFireAxeAuthoring.Load()?.WorldHeight ?? 1.15f;
        public Vector2 FootPivot => StormInkFireAxeAuthoring.Load()?.FootPivot ?? new Vector2(.5f, 5f / 128f);

        public static StormInkFireSwordAuthoring Load() => _default != null ? _default :
            _default = Resources.Load<StormInkFireSwordAuthoring>("StormInk/Actors/FireSwordAuthoring");

        internal static void SharedFireRigChanged() => _default?.InvalidateFrames();

        public Sprite Frame(int row, StormInkFireSwordMotionPhase phase, float progress, bool alternate)
        {
            if ((uint)row >= Rows) return null;
            int column = PoseColumn(phase, progress, alternate);
            return column >= 0 ? Frame(row, column) : null;
        }

        public int PoseColumn(StormInkFireSwordMotionPhase phase, float progress, bool alternate)
        {
            progress = Mathf.Clamp01(progress);
            var timing = alternate ? releaseBTiming : releaseATiming;
            int release = alternate ? 1 : 0;
            return phase switch
            {
                StormInkFireSwordMotionPhase.Windup => progress < Mathf.Clamp(timing.PassingAt, .1f, .9f)
                    ? release * 5 : release * 5 + 1,
                StormInkFireSwordMotionPhase.Strike => release * 5 + 2,
                StormInkFireSwordMotionPhase.Follow => release * 5 +
                    (progress < Mathf.Clamp(timing.RecoveryAt, .1f, .9f) ? 3 : 4),
                _ => -1
            };
        }

#if UNITY_EDITOR
        public void PreviewParameters(int column, out StormInkFireSwordMotionPhase phase, out float progress)
        {
            if ((uint)column >= Columns) throw new ArgumentOutOfRangeException(nameof(column));
            int pose = column % 5;
            bool alternate = column >= 5;
            var timing = alternate ? releaseBTiming : releaseATiming;
            float passingAt = Mathf.Clamp(timing.PassingAt, .1f, .9f);
            float recoveryAt = Mathf.Clamp(timing.RecoveryAt, .1f, .9f);
            switch (pose)
            {
                case 0: phase = StormInkFireSwordMotionPhase.Windup; progress = passingAt * .5f; break;
                case 1: phase = StormInkFireSwordMotionPhase.Windup; progress = (passingAt + 1f) * .5f; break;
                case 2: phase = StormInkFireSwordMotionPhase.Strike; progress = .5f; break;
                case 3: phase = StormInkFireSwordMotionPhase.Follow; progress = recoveryAt * .5f; break;
                default: phase = StormInkFireSwordMotionPhase.Follow; progress = (recoveryAt + 1f) * .5f; break;
            }
        }
#endif

        public Sprite Frame(int row, int column)
        {
            if ((uint)row >= Rows || (uint)column >= Columns) return null;
            EnsureFrames();
            return _frames == null ? null : _frames[row * Columns + column];
        }

        public float WeaponAngleOffset(int row, int column)
        {
            EnsurePoseOffsets();
            int index = PoseIndex(row, column);
            return _angleOffsets != null && index >= 0 ? _angleOffsets[index] : 0f;
        }

        public float BladeAngleOffset(int row, StormInkFireSwordMotionPhase phase, float progress,
            bool alternate, bool reduced)
        {
            progress = Mathf.Clamp01(progress);
            int release = alternate ? 1 : 0;
            int column = release * 5;
            if (phase == StormInkFireSwordMotionPhase.Strike)
                return WeaponAngleOffset(row, column + 2);
            if (reduced) return 0f;

            var timing = alternate ? releaseBTiming : releaseATiming;
            if (phase == StormInkFireSwordMotionPhase.Windup)
            {
                float passingAt = Mathf.Clamp(timing.PassingAt, .1f, .9f);
                return Mathf.Lerp(WeaponAngleOffset(row, column), WeaponAngleOffset(row, column + 1), progress / passingAt);
            }
            if (phase != StormInkFireSwordMotionPhase.Follow) return 0f;

            float recoveryAt = Mathf.Clamp(timing.RecoveryAt, .1f, .9f);
            if (progress < recoveryAt)
                return Mathf.Lerp(WeaponAngleOffset(row, column + 2), WeaponAngleOffset(row, column + 3), progress / recoveryAt);
            return Mathf.Lerp(WeaponAngleOffset(row, column + 3), WeaponAngleOffset(row, column + 4),
                (progress - recoveryAt) / (1f - recoveryAt));
        }

        public int WeaponDepthOffset(int row, int column)
        {
            EnsurePoseOffsets();
            int index = PoseIndex(row, column);
            return _depthOffsets != null && index >= 0 ? _depthOffsets[index] : 0;
        }

        static int PoseIndex(int row, int column) =>
            (uint)row < Rows && (uint)column < Columns ? row * Columns + column : -1;

        void EnsurePoseOffsets()
        {
            if (_poseOffsetsChecked) return;
            _poseOffsetsChecked = true;
            if (poses == null || poses.Length != Rows * Columns) return;
            _angleOffsets = new float[Rows * Columns];
            _depthOffsets = new int[Rows * Columns];
            var seen = new bool[Rows * Columns];
            foreach (var pose in poses)
            {
                int index = PoseIndex(pose.Row, pose.Column);
                if (index < 0 || seen[index])
                {
                    _angleOffsets = null;
                    _depthOffsets = null;
                    return;
                }
                seen[index] = true;
                _angleOffsets[index] = pose.WeaponAngleOffset;
                _depthOffsets[index] = pose.WeaponDepthOffset;
            }
        }

        public bool TryHand(Sprite body, bool left, out Vector2 uv)
        {
            uv = default;
            if (body == null || atlas == null || body.texture != atlas) return false;
            EnsureFrames();
            if (_frames == null) return false;

            var rect = body.rect;
            int column = Mathf.RoundToInt(rect.x / rect.width);
            int row = Rows - 1 - Mathf.RoundToInt(rect.y / rect.height);
            if ((uint)row >= Rows || (uint)column >= Columns) return false;
            int index = row * Columns + column;
            if (_frames[index] != body || index >= palms.Length) return false;

            var point = left ? palms[index].Left : palms[index].Right;
            int side = Mathf.RoundToInt(rect.width);
            uv = new Vector2((point.x + .5f) / side, 1f - (point.y + .5f) / side);
            return true;
        }

        void EnsureFrames()
        {
            if (_frames != null || atlas == null || atlas.width % Columns != 0 || atlas.height % Rows != 0 ||
                atlas.width / Columns != atlas.height / Rows) return;
            int side = atlas.width / Columns;
            _frames = new Sprite[Rows * Columns];
            for (int row = 0; row < Rows; row++)
            for (int column = 0; column < Columns; column++)
            {
                var rect = new Rect(column * side, (Rows - 1 - row) * side, side, side);
                var frame = Sprite.Create(atlas, rect, FootPivot, side * .89f, 0, SpriteMeshType.FullRect);
                frame.name = $"FireSword/{row}/{column}";
                frame.hideFlags = HideFlags.HideAndDontSave;
                _frames[row * Columns + column] = frame;
            }
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            _angleOffsets = null;
            _depthOffsets = null;
            _poseOffsetsChecked = false;
            InvalidateFrames();
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
