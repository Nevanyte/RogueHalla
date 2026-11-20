using System;
using System.Collections.Generic;
using Roguehalla.Client;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ArenaView = Roguehalla.Client.ArenaView;

namespace Roguehalla.Editor
{
    /// <summary>Isolated edit-mode stage using the production arena, actor and landmark renderers.</summary>
    public sealed class StormInkChurchStage : PreviewSceneStage
    {
        // Source blueprint faces south. LookCapture encodes swap axes, then reflect X and Y.
        static readonly int[] QuarterTurns = { 0, 3, 6, 5 };
        static readonly string[] FrontLabels = { "South", "West", "North", "East" };
        [SerializeField] int front;
        [SerializeField] bool mirrored, solidRoof, overlays;
        [SerializeField] float ambient = .86f;
        GameObject root;
        ArenaView arena;
        MapBlob map;
        Vector3 feet;
        Camera lightingCamera;

        [MenuItem("Tools/RogueHalla/Storm Ink/Church Art Studio")]
        static void Open()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (StageUtility.GetCurrentStage() is StormInkChurchStage) return;
            EditorWindow.GetWindow<SceneView>();
            StageUtility.GoToStage(CreateInstance<StormInkChurchStage>(), true);
        }

        [MenuItem("Tools/RogueHalla/Storm Ink/Church Art Studio", true)]
        static bool CanOpen() => !EditorApplication.isPlayingOrWillChangePlaymode;

        public static MapBlob PreviewMap(int quarterTurn, bool reflect)
        {
            if ((uint)quarterTurn >= QuarterTurns.Length) throw new ArgumentOutOfRangeException(nameof(quarterTurn));
            int orientation = QuarterTurns[quarterTurn];
            if (reflect) orientation ^= (orientation & 1) == 0 ? 2 : 4;
            return LookCapture.BuildChurchMap(orientation);
        }

        protected override GUIContent CreateHeaderContent() => new GUIContent("Church Art Studio");

        protected override bool OnOpenStage()
        {
            if (!base.OnOpenStage()) return false;
            try
            {
                Build();
                SceneView.duringSceneGui += DrawControls;
                EditorApplication.playModeStateChanged += OnPlayMode;
                EditorApplication.delayCall += FrameRoom;
                return true;
            }
            catch
            {
                ClearRoom();
                throw;
            }
        }

        protected override void OnCloseStage()
        {
            SceneView.duringSceneGui -= DrawControls;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.delayCall -= FrameRoom;
            EditorApplication.delayCall -= Rebuild;
            ClearRoom();
            base.OnCloseStage();
        }

        void OnPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode && StageUtility.GetCurrentStage() == this)
                StageUtility.GoToMainStage();
        }

        void Build()
        {
            map = PreviewMap(front, mirrored);
            root = new GameObject("Storm Ink church");
            SceneManager.MoveGameObjectToScene(root, scene);
            arena = LookCapture.BuildStudioArena(map, root);
            var anchor = map.Anchors[0];
            int tx = anchor.Centre.X, ty = anchor.Centre.Y;
            // The signed camp centre is on the nave floor. Both fighters stand on delivered walkable cells.
            var landmark = LookCapture.StudioLandmarks(arena).Get(0);
            var other = new Vector2Int(tx, ty);
            foreach (var delta in new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down })
            {
                var tile = new Vector2Int(tx, ty) + delta * 2;
                if (landmark.IsInterior(tile.x, tile.y) && map.At(tile.x, tile.y) == MapBlob.Floor)
                {
                    other = tile;
                    break;
                }
            }
            float x = (tx + .5f) * 32f, y = (ty + .5f) * 32f;
            feet = ArenaView.WorldPoint(x, y);
            arena.Regions.SnapLocal(x, y);
            var bailiff = new InterpActor { Id = 2, Kind = ActorKind.RegionalMob, MobType = MobTypeId.Bailiff,
                Faction = FactionId.Sentenced, Weapon = WeaponId.Sword, Status = ActorStatus.Alive, HpPct = 100,
                Facing = Mathf.PI, X = (other.x + .5f) * 32f, Y = (other.y + .5f) * 32f };
            var fire = new SelfView { Id = 1, Faction = FactionId.Fire, Weapon = WeaponId.Axe,
                Status = ActorStatus.Alive, Hp = 100, MaxHp = 100, X = x, Y = y };
            arena.Present(new List<InterpActor> { bailiff }, fire, null, .6f);

            var cameraObject = new GameObject("Gameplay lighting reference");
            cameraObject.transform.SetParent(root.transform, false);
            lightingCamera = cameraObject.AddComponent<Camera>();
            lightingCamera.enabled = false;
            lightingCamera.orthographic = true;
            lightingCamera.orthographicSize = 9f;
            lightingCamera.aspect = 16f / 9f;
            arena.Follow(lightingCamera, x, y);
            ApplyAppearance();
        }

        void ApplyAppearance()
        {
            var landmarks = LookCapture.StudioLandmarks(arena);
            landmarks.Tick(1f); // consume pending presence observations before explicitly choosing the inspection state
            if (!solidRoof) landmarks.NoteActor(feet);
            landmarks.Tick(1f);
            arena.Atmosphere.SetAmbientBase(ambient);
            arena.Atmosphere.OnFollow(lightingCamera, 1f);
            SceneView.RepaintAll();
        }

        void ClearRoom()
        {
            if (root != null) DestroyImmediate(root);
            root = null;
            arena = null;
        }

        internal void ScheduleRebuild()
        {
            EditorApplication.delayCall -= Rebuild;
            EditorApplication.delayCall += Rebuild;
        }

        void Rebuild()
        {
            if (this == null || !scene.IsValid()) return;
            ClearRoom();
            Build();
            SceneView.RepaintAll();
        }

        void FrameRoom()
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null || root == null || StageUtility.GetCurrentStage() != this) return;
            view.in2DMode = true;
            view.orthographic = true;
            view.sceneLighting = true;
            // SceneView.size is half the view diagonal. Match the gameplay camera's 9-unit half-height.
            float aspect = view.camera.aspect;
            view.LookAtDirect(lightingCamera.transform.position + Vector3.forward * 30f, Quaternion.identity,
                9f * Mathf.Sqrt(1f + aspect * aspect));
            view.Repaint();
        }

        void DrawControls(SceneView view)
        {
            if (StageUtility.GetCurrentStage() != this || arena == null) return;
            if (overlays && Event.current.type == EventType.Repaint) DrawMapOverlay();
            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(10, 10, 295, 178), EditorStyles.helpBox);
            EditorGUI.BeginChangeCheck();
            front = EditorGUILayout.Popup("Graveyard entrance", front, FrontLabels);
            mirrored = EditorGUILayout.Toggle("Reflect layout", mirrored);
            if (EditorGUI.EndChangeCheck()) ScheduleRebuild();
            EditorGUI.BeginChangeCheck();
            solidRoof = EditorGUILayout.Toggle("Opaque roof", solidRoof);
            ambient = EditorGUILayout.Slider("Ambient light", ambient, .15f, 1.25f);
            if (EditorGUI.EndChangeCheck()) ApplyAppearance();
            overlays = EditorGUILayout.Toggle("Map and fighter footprints", overlays);
            if (GUILayout.Button("Frame at gameplay zoom")) FrameRoom();
            GUILayout.EndArea();
            Handles.EndGUI();
        }

        void DrawMapOverlay()
        {
            var anchor = map.Anchors[0];
            var f = anchor.Footprint;
            using (new Handles.DrawingScope(new Color(.94f, .43f, .26f, .65f)))
            {
                for (int y = f.Y; y < f.Y + f.H; y++)
                for (int x = f.X; x < f.X + f.W; x++)
                {
                    if (map.At(x, y) == MapBlob.Floor) continue;
                    DrawTile(x, y);
                }
            }
            using (new Handles.DrawingScope(Color.cyan))
            {
                foreach (var tile in anchor.Entrance) DrawTile(tile.X, tile.Y);
                foreach (var r in root.GetComponentsInChildren<SpriteRenderer>())
                    if (r.enabled && r.name == "Faction footprint") Handles.DrawWireCube(r.bounds.center, r.bounds.size);
            }
        }

        static void DrawTile(int x, int y)
        {
            Handles.DrawWireCube(ArenaView.WorldPoint((x + .5f) * 32, (y + .5f) * 32),
                new Vector3(1f, ArenaView.GroundSquash, 0f));
        }
    }
}
