using System;
using System.Collections.Generic;
using System.IO;
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
        static readonly string[] FighterFacings = { "South", "Southeast", "East", "Northeast", "North", "Northwest", "West", "Southwest" };
        static readonly string[] FireWeaponChoices = { "Axe", "Sword", "Bow" };
        static readonly string[] FireMotionPhases = { "Draw / windup", "Release", "Recovery" };
        [SerializeField] int fighterFacing = 1;
        [SerializeField] int fireWeaponChoice;
        [SerializeField] StormInkFireSwordMotionPhase firePosePhase;
        [SerializeField, Range(0f, 1f)] float firePoseProgress = .5f;
        [SerializeField] bool firePoseAlternate;
        static readonly string[] FrontLabels = { "South", "West", "North", "East" };
        [SerializeField] int front;
        [SerializeField] bool mirrored, solidRoof, overlays;
        [SerializeField] float ambient = .86f;
        GameObject root;
        ArenaView arena;
        MapBlob map;
        List<MapTile> passages;
        Vector3 feet, bailiffFeet;
        Footprint fireFootprint, bailiffFootprint;
        Camera lightingCamera;
        bool reframeAfterRebuild;
        static readonly int GroundPalette = Shader.PropertyToID("_PalBase");
        readonly List<Vector4> paletteProbe = new List<Vector4>(8);
        Material groundMaterial;
        bool inspectPalette;
        SelfView fighter;
        readonly List<InterpActor> opponents = new List<InterpActor>(1);

        [MenuItem("Tools/RogueHalla/Storm Ink/Church Art Studio")]
        static void Open()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (StageUtility.GetCurrentStage() is StormInkChurchStage) return;
            if (!Application.isBatchMode) EditorWindow.GetWindow<SceneView>();
            StageUtility.GoToStage(CreateInstance<StormInkChurchStage>(), true);
        }

        [MenuItem("Tools/RogueHalla/Storm Ink/Church Art Studio", true)]
        static bool CanOpen() => !EditorApplication.isPlayingOrWillChangePlaymode;

        public static MapBlob PreviewMap(int quarterTurn, bool reflect) =>
            PreviewRoom(quarterTurn, reflect, out _);

        static MapBlob PreviewRoom(int quarterTurn, bool reflect, out List<MapTile> doorTiles)
        {
            if ((uint)quarterTurn >= QuarterTurns.Length) throw new ArgumentOutOfRangeException(nameof(quarterTurn));
            int orientation = QuarterTurns[quarterTurn];
            if (reflect) orientation ^= (orientation & 1) == 0 ? 2 : 4;
            // The symmetric public blueprint carries no mirror flag. Choose a legitimate translated
            // fixture that the ordinary runtime resolves to the requested cosmetic reflection.
            for (int shift = -8; shift <= 8; shift++)
            {
                var candidate = LookCapture.BuildChurchMap(orientation, shift, out doorTiles);
                if (ChurchArtOrientation.Resolve(candidate.Anchors[0]).Mirrored == reflect) return candidate;
            }
            throw new InvalidOperationException("Unable to construct the requested church reflection.");
        }

        protected override GUIContent CreateHeaderContent() => new GUIContent("Church Art Studio");

        protected override bool OnOpenStage()
        {
            if (!base.OnOpenStage()) return false;
            try
            {
                Build();
                EditorApplication.update += RestoreSavedPreview;
                SceneView.beforeSceneGui += RestoreBeforeSceneGui;
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
            EditorApplication.update -= RestoreSavedPreview;
            SceneView.beforeSceneGui -= RestoreBeforeSceneGui;
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
            map = PreviewRoom(front, mirrored, out passages);
            var fixture = ArchetypeFixture.Read();
            fireFootprint = fixture.Foot(fixture.assign.player.Fire);
            bailiffFootprint = fixture.Foot(fixture.assign.mob.bailiff);
            root = new GameObject("Storm Ink church");
            SceneManager.MoveGameObjectToScene(root, scene);
            arena = LookCapture.BuildStudioArena(map, root);
            groundMaterial = arena.PreviewGroundMaterial;
            inspectPalette = HasGroundPalette();
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
            fighter = new SelfView { Id = 1, Faction = FactionId.Fire,
                Weapon = SelectedFireWeapon,
                Status = ActorStatus.Alive, Hp = 100, MaxHp = 100, X = x, Y = y };
            bailiffFeet = ArenaView.WorldPoint(bailiff.X, bailiff.Y);
            opponents.Clear();
            opponents.Add(bailiff);
            RefreshCast();
            // A newly opened stage is a settled snapshot. Its remote fighter must be visible on the first editor frame.
            arena.ResetTransientPresentation();

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

        internal void RefreshCast()
        {
            if (arena == null) return;
            arena.SetStudioFirePose(fighter.Id,
                fireWeaponChoice == 0 ? (WeaponId?)null : SelectedFireWeapon,
                firePosePhase, firePoseProgress, firePoseAlternate);
            arena.Present(opponents, fighter, null, (2 - fighterFacing) * Mathf.PI / 4f);
            if (lightingCamera != null) ApplyAppearance();
        }

        WeaponId SelectedFireWeapon => fireWeaponChoice == 1 ? WeaponId.Sword :
            fireWeaponChoice == 2 ? WeaponId.Bow : WeaponId.Axe;

        public void PreviewFireLoadout(WeaponId weapon, int facingRow, int swordPose)
        {
            if ((uint)facingRow >= FighterFacings.Length) throw new ArgumentOutOfRangeException(nameof(facingRow));
            if (weapon != WeaponId.Axe && weapon != WeaponId.Sword && weapon != WeaponId.Bow)
                throw new ArgumentOutOfRangeException(nameof(weapon));
            if (weapon != WeaponId.Axe && (uint)swordPose >= 10)
                throw new ArgumentOutOfRangeException(nameof(swordPose));
            fireWeaponChoice = weapon == WeaponId.Sword ? 1 : weapon == WeaponId.Bow ? 2 : 0;
            fighterFacing = facingRow;
            if (weapon == WeaponId.Sword)
            {
                StormInkFireSwordAuthoring.Load().PreviewParameters(swordPose, out firePosePhase, out firePoseProgress);
                firePoseAlternate = swordPose >= 5;
            }
            else if (weapon == WeaponId.Bow)
            {
                int pose = swordPose % 5;
                firePoseAlternate = swordPose >= 5;
                firePosePhase = pose <= 2 ? StormInkFireSwordMotionPhase.Windup :
                    pose == 3 ? StormInkFireSwordMotionPhase.Strike : StormInkFireSwordMotionPhase.Follow;
                firePoseProgress = pose == 0 ? .1f : pose == 1 ? .5f : pose == 2 ? 1f :
                    pose == 3 ? .5f : .85f;
            }
            if (fighter != null) fighter.Weapon = weapon;
            RefreshCast();
        }

        public void PreviewFireSampler(WeaponId weapon, int facingRow, StormInkFireSwordMotionPhase phase,
            float progress, bool alternate)
        {
            if ((uint)facingRow >= FighterFacings.Length) throw new ArgumentOutOfRangeException(nameof(facingRow));
            if (weapon != WeaponId.Sword && weapon != WeaponId.Bow)
                throw new ArgumentOutOfRangeException(nameof(weapon));
            fireWeaponChoice = weapon == WeaponId.Sword ? 1 : 2;
            fighterFacing = facingRow;
            firePosePhase = phase;
            firePoseProgress = Mathf.Clamp01(progress);
            firePoseAlternate = alternate;
            if (fighter != null) fighter.Weapon = weapon;
            RefreshCast();
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
            if (arena != null) arena.ReleasePresentation();
            if (root != null) DestroyImmediate(root);
            root = null;
            arena = null;
            lightingCamera = null;
        }

        public void ShowLayout(int quarterTurn, bool reflect)
        {
            if ((uint)quarterTurn >= QuarterTurns.Length) throw new ArgumentOutOfRangeException(nameof(quarterTurn));
            if (front == quarterTurn && mirrored == reflect) return;
            front = quarterTurn;
            mirrored = reflect;
            reframeAfterRebuild = true;
            ScheduleRebuild();
        }

        internal void RefreshMasonry()
        {
            RegionWallAtlas.RefreshNaveFinish();
            ScheduleRebuild();
        }

        void ScheduleRebuild()
        {
            EditorApplication.delayCall -= Rebuild;
            EditorApplication.delayCall += Rebuild;
        }

        void Rebuild()
        {
            if (this == null || !scene.IsValid()) return;
            var picked = Selection.activeGameObject;
            var oldPart = picked != null && picked.scene == scene ? picked.GetComponentInParent<ChurchArtPlacement>() : null;
            string slot = oldPart != null ? oldPart.SlotId : null;
            var source = picked != null && picked.scene == scene ? picked.GetComponentInParent<StormInkArtistSource>() : null;
            var sourceKind = source != null ? source.Kind : StormInkArtistSourceKind.None;
            ClearRoom();
            Build();
            if (slot != null)
            {
                foreach (var part in root.GetComponentsInChildren<ChurchArtPlacement>(true))
                    if (part.SlotId == slot) { Selection.activeGameObject = part.gameObject; break; }
            }
            else if (sourceKind != StormInkArtistSourceKind.None)
            {
                foreach (var owner in root.GetComponentsInChildren<StormInkArtistSource>(true))
                    if (owner.Kind == sourceKind) { Selection.activeGameObject = owner.gameObject; break; }
            }
            if (reframeAfterRebuild) FrameRoom();
            reframeAfterRebuild = false;
            SceneView.RepaintAll();
        }

        // Unity clears nonserialized shader tables on transient materials when an asset is saved.
        // The existing material is checked without allocating. Ordinary Inspector edits keep the room intact.
        bool HasGroundPalette()
        {
            if (groundMaterial == null) return false;
            paletteProbe.Clear();
            groundMaterial.GetVectorArray(GroundPalette, paletteProbe);
            return paletteProbe.Count > 0;
        }

        void RestoreBeforeSceneGui(SceneView _) => RestoreSavedPreview();

        void RestoreSavedPreview()
        {
            if (!inspectPalette || root == null || HasGroundPalette()) return;
            EditorApplication.delayCall -= Rebuild;
            Rebuild();
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
            GUILayout.BeginArea(new Rect(10, 10, 310, 310), EditorStyles.helpBox);
            EditorGUI.BeginChangeCheck();
            int nextFront = EditorGUILayout.Popup("Graveyard entrance", front, FrontLabels);
            bool nextMirror = EditorGUILayout.Toggle("Reflect layout", mirrored);
            if (EditorGUI.EndChangeCheck()) ShowLayout(nextFront, nextMirror);
            EditorGUI.BeginChangeCheck();
            solidRoof = EditorGUILayout.Toggle("Opaque roof", solidRoof);
            ambient = EditorGUILayout.Slider("Ambient light", ambient, .15f, 1.25f);
            if (EditorGUI.EndChangeCheck()) ApplyAppearance();
            EditorGUI.BeginChangeCheck();
            int nextWeapon = EditorGUILayout.Popup("Fire weapon", fireWeaponChoice, FireWeaponChoices);
            fighterFacing = EditorGUILayout.Popup("Fire facing", fighterFacing, FighterFacings);
            if (nextWeapon != fireWeaponChoice)
            {
                fireWeaponChoice = nextWeapon;
                fighter.Weapon = SelectedFireWeapon;
            }
            if (fireWeaponChoice != 0)
            {
                firePosePhase = (StormInkFireSwordMotionPhase)EditorGUILayout.Popup(
                    "Motion phase", (int)firePosePhase, FireMotionPhases);
                firePoseProgress = EditorGUILayout.Slider(
                    firePosePhase == StormInkFireSwordMotionPhase.Windup ? "Draw progress" :
                    firePosePhase == StormInkFireSwordMotionPhase.Strike ? "Release progress" : "Recovery progress",
                    firePoseProgress, 0f, 1f);
                firePoseAlternate = EditorGUILayout.Toggle("Alternate performance", firePoseAlternate);
                if (SelectedFireWeapon == WeaponId.Bow)
                {
                    var bowArt = StormInkFireBowAuthoring.Load();
                    if (bowArt != null && !bowArt.IsReady(fighterFacing))
                        EditorGUILayout.HelpBox("Bow performance art is not ready for this facing. The normal Fire body and shared bow fallback are shown.",
                            MessageType.Info);
                }
            }
            if (EditorGUI.EndChangeCheck()) RefreshCast();
            overlays = EditorGUILayout.Toggle("Map and fighter footprints", overlays);
            if (GUILayout.Button("Frame at gameplay zoom")) FrameRoom();
            GUILayout.BeginHorizontal();
            string bodyName = fireWeaponChoice == 1 ? "Fire sword body" :
                fireWeaponChoice == 2 ? "Fire bow body" : "Fire body";
            string weaponName = fireWeaponChoice == 1 ? "Shared sword" :
                fireWeaponChoice == 2 ? "Shared bow" : "Shared axe";
            if (GUILayout.Button(bodyName)) Selection.activeObject = fireWeaponChoice == 1
                ? StormInkFireSwordAuthoring.Load()
                : fireWeaponChoice == 2 ? StormInkFireBowAuthoring.Load() : StormInkFireAxeAuthoring.Load();
            if (GUILayout.Button(weaponName)) Selection.activeObject = fireWeaponChoice == 1
                ? StormInkSwordArtAuthoring.Load()
                : fireWeaponChoice == 2 ? StormInkBowArtAuthoring.Load() : StormInkAxeArtAuthoring.Load();
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Nave wall finish")) Selection.activeObject = ChurchMasonryArt.Load();
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
                foreach (var tile in passages) DrawTile(tile.X, tile.Y);
            }
            using (new Handles.DrawingScope(Color.yellow))
            {
                DrawFootprint(feet, fireFootprint);
                DrawFootprint(bailiffFeet, bailiffFootprint);
            }
        }

        // Read the same Rust fixture as the client hitbox tests. Resizing the painting cannot move these bounds.
        #pragma warning disable CS0649
        [Serializable] sealed class ArchetypeFixture
        {
            [Serializable] public class Player { public int Fire; }
            [Serializable] public class Mob { public int bailiff; }
            [Serializable] public class Assign { public Player player; public Mob mob; }
            [Serializable] public class Hull { public string kind; public float r; public float[] foot; }
            [Serializable] public class Row { public int id; public Hull hull; }
            public Assign assign;
            public Row[] archetypes;
            public static ArchetypeFixture Read()
            {
                string file = Path.Combine(Application.dataPath, "../../../protocol/fixtures/archetypes_v1.json");
                return JsonUtility.FromJson<ArchetypeFixture>(File.ReadAllText(file));
            }
            public Footprint Foot(int id)
            {
                var hull = Array.Find(archetypes, row => row.id == id).hull;
                return hull.kind == "circle" ? Footprint.Disc(hull.r) : Footprint.Box(hull.foot[0], hull.foot[1]);
            }
        }
        #pragma warning restore CS0649

        static void DrawFootprint(Vector3 at, Footprint foot)
        {
            var half = new Vector3((float)foot.Hx * ArenaView.Scale, (float)foot.Hy * ArenaView.Scale * ArenaView.GroundSquash, 1);
            if (foot.IsBox)
            {
                Handles.DrawWireCube(at, new Vector3(half.x * 2f, half.y * 2f, 0f));
                return;
            }
            using (new Handles.DrawingScope(Matrix4x4.TRS(at, Quaternion.identity, half)))
                Handles.DrawWireDisc(Vector3.zero, Vector3.forward, 1f);
        }

        static void DrawTile(int x, int y)
        {
            Handles.DrawWireCube(ArenaView.WorldPoint((x + .5f) * 32, (y + .5f) * 32),
                new Vector3(1f, ArenaView.GroundSquash, 0f));
        }
    }
}
