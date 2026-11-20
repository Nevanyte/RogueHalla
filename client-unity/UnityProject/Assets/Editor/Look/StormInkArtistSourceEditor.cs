using Roguehalla.Client;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Roguehalla.Editor
{
    /// <summary>Edits shared authoring assets from the actual sprite and socket selected in the scene.</summary>
    [CustomEditor(typeof(StormInkArtistSource))]
    public sealed class StormInkArtistSourceEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var view = (StormInkArtistSource)target;
            var root = new VisualElement();
            var body = view.FireBody;
            var owner = view.BodyPainting != null ? view.BodyPainting : view.WeaponSource;
            if (owner == null || view.Sprite == null) return root;
            var source = new SerializedObject(owner);
            void AddField(SerializedProperty prop, string label)
            {
                var field = new PropertyField(prop, label);
                root.Add(field);
                field.BindProperty(prop);
            }
            if (view.BodyPainting != null)
            {
                if (body != null)
                {
                    var sizing = owner != body ? new SerializedObject(body) : source;
                    AddField(sizing.FindProperty("worldHeight"), "Fighter height (world units)");
                    AddField(sizing.FindProperty("footPivot"), "Foot anchor (UV)");
                }
                if (!Application.isPlaying)
                {
                    int index = PalmIndex(view);
                    var palms = source.FindProperty("palms");
                    if (palms != null && (uint)index < palms.arraySize)
                    {
                        var pair = palms.GetArrayElementAtIndex(index);
                        AddField(pair.FindPropertyRelative("Left"), "Left palm (pixels from top left)");
                        AddField(pair.FindPropertyRelative("Right"), "Right palm (pixels from top left)");
                    }
                }
            }
            else
            {
                var grips = source.FindProperty("grips");
                if (view.PoseRow >= 0 && view.PoseRow < grips.arraySize)
                    AddField(grips.GetArrayElementAtIndex(view.PoseRow), "Shared grip (UV)");
                if (owner is StormInkBowArtAuthoring)
                {
                    var nocks = source.FindProperty("nocks");
                    if ((uint)view.PoseRow < nocks.arraySize)
                    {
                        var pair = nocks.GetArrayElementAtIndex(view.PoseRow);
                        AddField(pair.FindPropertyRelative("Top"), "Upper bow nock (UV)");
                        AddField(pair.FindPropertyRelative("Bottom"), "Lower bow nock (UV)");
                    }
                }
                if (view.IsDropped)
                    AddField(source.FindProperty("groundCentre"), "Pickup art offset (world units)");
            }
            root.Add(new Button(() => Selection.activeObject = owner) { text = "Select source asset" });
            // Rebind to a new heading or pooled occupant without interrupting field editing every frame.
            var initialRow = view.PoseRow;
            var initialKind = view.Kind;
            var initialSprite = view.Sprite;
            root.schedule.Execute(() =>
            {
                if (view != null && (initialRow != view.PoseRow || initialKind != view.Kind ||
                    (!Application.isPlaying && initialSprite != view.Sprite)))
                    ActiveEditorTracker.sharedTracker.ForceRebuild();
            }).Every(250);
            return root;
        }

        void OnSceneGUI()
        {
            var source = (StormInkArtistSource)target;
            if (source.Sprite == null || source.Renderer == null) return;
            var sprite = source.Sprite;
            var renderer = source.Renderer;
            var size = sprite.rect.size;
            Vector3 World(Vector2 uv)
            {
                var p = (Vector2.Scale(uv, size) - sprite.pivot) / sprite.pixelsPerUnit;
                return renderer.transform.TransformPoint(new Vector3(renderer.flipX ? -p.x : p.x, renderer.flipY ? -p.y : p.y));
            }
            Vector2 Uv(Vector3 point)
            {
                var p = renderer.transform.InverseTransformPoint(point);
                return new Vector2(((renderer.flipX ? -p.x : p.x) * sprite.pixelsPerUnit + sprite.pivot.x) / size.x,
                    ((renderer.flipY ? -p.y : p.y) * sprite.pixelsPerUnit + sprite.pivot.y) / size.y);
            }
            using (new Handles.DrawingScope(Color.cyan))
                Handles.DrawWireCube(renderer.bounds.center, renderer.bounds.size);
            if (source.FireBody != null)
            {
                Vector3 foot = World(source.FireBody.FootPivot);
                using (new Handles.DrawingScope(Color.cyan))
                {
                    Handles.Label(foot + Vector3.down * .09f, "Foot anchor");
                    EditorGUI.BeginChangeCheck();
                    var movedFoot = Handles.Slider2D(foot, Vector3.forward, Vector3.right, Vector3.up,
                        HandleUtility.GetHandleSize(foot) * .045f, Handles.RectangleHandleCap, Vector2.zero);
                    if (EditorGUI.EndChangeCheck())
                        Edit(source.FireBody, "footPivot", prop => prop.vector2Value = Uv(movedFoot));
                    var crown = World(new Vector2(source.FireBody.FootPivot.x, 1f));
                    EditorGUI.BeginChangeCheck();
                    var movedCrown = Handles.Slider(crown, Vector3.up, HandleUtility.GetHandleSize(crown) * .055f,
                        Handles.ConeHandleCap, 0f);
                    if (EditorGUI.EndChangeCheck() && Mathf.Abs(crown.y - foot.y) > .001f)
                        Edit(source.FireBody, "worldHeight", prop => prop.floatValue =
                            Mathf.Max(.1f, source.FireBody.WorldHeight * (movedCrown.y - foot.y) / (crown.y - foot.y)));
                }
                foreach (bool left in new[] { false, true })
                {
                    if (!source.TryBodyPalm(left, out var palm)) continue;
                    using (new Handles.DrawingScope(left ? Color.cyan : Color.yellow))
                    {
                        var at = World(palm);
                        Handles.Label(at + (left ? Vector3.left : Vector3.right) * .09f, left ? "L" : "R");
                        EditorGUI.BeginChangeCheck();
                        var moved = Handles.Slider2D(at, Vector3.forward, Vector3.right, Vector3.up,
                            HandleUtility.GetHandleSize(at) * .03f, Handles.CircleHandleCap, Vector2.zero);
                        if (EditorGUI.EndChangeCheck())
                        {
                            var uv = Uv(moved);
                            int index = PalmIndex(source);
                            var pixel = new Vector2Int(Mathf.RoundToInt(uv.x * size.x - .5f),
                                Mathf.RoundToInt((1f - uv.y) * size.y - .5f));
                            Edit(source.BodyPainting, $"palms.Array.data[{index}].{(left ? "Left" : "Right")}",
                                prop => prop.vector2IntValue = pixel);
                        }
                    }
                }
            }
            else if (source.WeaponSource != null)
            {
                using var settings = new SerializedObject(source.WeaponSource);
                var grips = settings.FindProperty("grips");
                if ((uint)source.PoseRow >= grips.arraySize) return;
                void Socket(SerializedProperty point, string label, Color color, float radius)
                {
                    using var scope = new Handles.DrawingScope(color);
                    var position = World(point.vector2Value);
                    Handles.Label(position + Vector3.up * .09f, label);
                    EditorGUI.BeginChangeCheck();
                    var moved = Handles.Slider2D(position, Vector3.forward, Vector3.right, Vector3.up,
                        HandleUtility.GetHandleSize(position) * radius, Handles.CircleHandleCap, Vector2.zero);
                    if (!EditorGUI.EndChangeCheck()) return;
                    point.vector2Value = Uv(moved);
                    settings.ApplyModifiedProperties();
                    ChurchArtEditorRefresh.Schedule();
                }

                Socket(grips.GetArrayElementAtIndex(source.PoseRow),
                    $"Shared {source.Weapon.ToString().ToLowerInvariant()} grip", Color.yellow, .05f);
                if (source.WeaponSource is StormInkBowArtAuthoring)
                {
                    var nocks = settings.FindProperty("nocks");
                    if ((uint)source.PoseRow >= nocks.arraySize) return;
                    var pair = nocks.GetArrayElementAtIndex(source.PoseRow);
                    Socket(pair.FindPropertyRelative("Top"), "Upper string nock", Color.cyan, .035f);
                    Socket(pair.FindPropertyRelative("Bottom"), "Lower string nock", Color.cyan, .035f);
                }
            }
        }

        static int PalmIndex(StormInkArtistSource source)
        {
            var sprite = source.Sprite;
            int columns = Mathf.RoundToInt(sprite.texture.width / sprite.rect.width);
            return source.PoseRow * columns + Mathf.RoundToInt(sprite.rect.x / sprite.rect.width);
        }

        static void Edit(Object asset, string field, System.Action<SerializedProperty> change)
        {
            using (var owner = new SerializedObject(asset))
            {
                change(owner.FindProperty(field));
                owner.ApplyModifiedProperties();
            }
            ChurchArtEditorRefresh.Schedule();
        }
    }
}
