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
            var axe = view.SharedAxe;
            var owner = body != null ? (Object)body : axe;
            if (owner == null || view.Sprite == null) return root;
            var source = new SerializedObject(owner);
            void AddField(SerializedProperty prop, string label)
            {
                var field = new PropertyField(prop, label);
                root.Add(field);
                field.BindProperty(prop);
            }
            if (body != null)
            {
                AddField(source.FindProperty("worldHeight"), "Fighter height (world units)");
                AddField(source.FindProperty("footPivot"), "Foot anchor (UV)");
            }
            else
            {
                var grips = source.FindProperty("grips");
                if (view.PoseRow >= 0 && view.PoseRow < grips.arraySize)
                    AddField(grips.GetArrayElementAtIndex(view.PoseRow), "Shared grip (UV)");
                if (view.Kind == StormInkArtistSourceKind.DroppedAxe)
                    AddField(source.FindProperty("groundCentre"), "Pickup art offset (world units)");
            }
            root.Add(new Button(() => Selection.activeObject = owner) { text = "Select source asset" });
            // Rebind to a new heading or pooled occupant without interrupting field editing every frame.
            var initialRow = view.PoseRow;
            var initialKind = view.Kind;
            root.schedule.Execute(() =>
            {
                if (view != null && (initialRow != view.PoseRow || initialKind != view.Kind))
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
                Handles.Label(foot, "Feet");
                foreach (bool left in new[] { false, true })
                {
                    if (!source.TryBodyPalm(left, out var palm)) continue;
                    using (new Handles.DrawingScope(left ? Color.cyan : Color.yellow))
                    {
                        var at = World(palm);
                        Handles.DrawSolidDisc(at, Vector3.forward, HandleUtility.GetHandleSize(at) * .018f);
                        Handles.Label(at + (left ? Vector3.left : Vector3.right) * .09f, left ? "L" : "R");
                    }
                }
            }
            else if (source.SharedAxe != null)
            {
                Vector3 grip = World(source.HeldGripUv);
                using (new Handles.DrawingScope(Color.yellow))
                {
                    Handles.Label(grip + Vector3.up * .09f, "Shared axe grip");
                    EditorGUI.BeginChangeCheck();
                    var moved = Handles.Slider2D(grip, Vector3.forward, Vector3.right, Vector3.up,
                        HandleUtility.GetHandleSize(grip) * .05f, Handles.CircleHandleCap, Vector2.zero);
                    if (EditorGUI.EndChangeCheck())
                    {
                        using (var owner = new SerializedObject(source.SharedAxe))
                        {
                            owner.FindProperty("grips").GetArrayElementAtIndex(source.PoseRow).vector2Value = Uv(moved);
                            owner.ApplyModifiedProperties();
                        }
                        ChurchArtEditorRefresh.Schedule();
                    }
                }
            }
        }
    }
}
