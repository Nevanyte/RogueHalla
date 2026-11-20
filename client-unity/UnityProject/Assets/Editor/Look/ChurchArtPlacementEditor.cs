using System;
using Roguehalla.Client;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Roguehalla.Editor
{
    /// <summary>Edits the persistent painting or reusable slot selected through a generated church part.</summary>
    [CustomEditor(typeof(ChurchArtPlacement))]
    public sealed class ChurchArtPlacementEditor : UnityEditor.Editor
    {
        const string ScopePreference = "RogueHalla.StormInk.Church.EditSlot";
        ChurchArtPlacement placement;
        VisualElement fields;
        bool editSlot;
        SerializedObject defaults, layout;

        public override VisualElement CreateInspectorGUI()
        {
            placement = (ChurchArtPlacement)target;
            var root = new VisualElement();
            if (placement.Definition == null || placement.Layout == null)
            {
                root.Add(new HelpBox("This part needs an art definition and church layout.", HelpBoxMessageType.Warning));
                return root;
            }
            defaults = new SerializedObject(placement.Definition);
            layout = new SerializedObject(placement.Layout);
            root.Add(new Label(placement.SlotId) { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            var scopes = new Toolbar();
            root.Add(scopes);
            var shared = new ToolbarToggle { text = "Asset defaults" };
            var local = new ToolbarToggle { text = "This slot" };
            scopes.Add(shared);
            scopes.Add(local);
            fields = new VisualElement();
            root.Add(fields);
            void SelectScope(bool slot)
            {
                editSlot = slot;
                SessionState.SetBool(ScopePreference, slot);
                shared.SetValueWithoutNotify(!slot);
                local.SetValueWithoutNotify(slot);
                RebindFields();
            }
            shared.RegisterValueChangedCallback(_ => SelectScope(false));
            local.RegisterValueChangedCallback(_ => SelectScope(true));
            SelectScope(SessionState.GetBool(ScopePreference, false));
            var source = new Button(() => Selection.activeObject = editSlot ? (UnityEngine.Object)placement.Layout : placement.Definition)
                { text = "Select source asset" };
            root.Add(source);
            root.RegisterCallback<AttachToPanelEvent>(_ => Undo.undoRedoPerformed += RebindFields);
            root.RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= RebindFields);
            return root;
        }

        void RebindFields()
        {
            if (placement == null || fields == null) return;
            defaults.Update();
            layout.Update();
            fields.Unbind();
            fields.Clear();
            var definition = placement.Definition;
            var defaultValues = defaults.FindProperty(nameof(ChurchArtDefinition.Defaults));
            int slotIndex = FindSlot(placement.Layout, placement.SlotId);
            var row = slotIndex < 0 ? null : layout.FindProperty(nameof(ChurchInteriorDefinition.SlotOverrides)).GetArrayElementAtIndex(slotIndex);
            var mask = slotIndex < 0 ? ChurchArtOverrideFields.None : placement.Layout.SlotOverrides[slotIndex].OverrideFields;
            foreach (ChurchArtOverrideFields field in Enum.GetValues(typeof(ChurchArtOverrideFields)))
            {
                if (!Visible(field, definition)) continue;
                bool overridden = (field & mask) != 0;
                var source = editSlot && overridden ? row.FindPropertyRelative(nameof(ChurchArtSlotOverride.Values)) : defaultValues;
                var property = source.FindPropertyRelative(field.ToString());
                var line = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                var editor = new PropertyField(property, FieldLabel(field)) { style = { flexGrow = 1, flexBasis = 0 } };
                editor.SetEnabled(!editSlot || overridden);
                editor.BindProperty(property);
                line.Add(editor);
                if (editSlot)
                {
                    var action = new Button(() =>
                    {
                        SetOverride(placement.Layout, placement.Definition, placement.SlotId, field, !overridden);
                        RebindFields();
                    }) { text = overridden ? "Reset" : "Override", tooltip = overridden ? "Reset to asset default" : "Override the inherited value for this slot" };
                    line.Add(action);
                }
                fields.Add(line);
            }
        }

        static bool Visible(ChurchArtOverrideFields field, ChurchArtDefinition definition)
        {
            switch (field)
            {
                case ChurchArtOverrideFields.None:
                case ChurchArtOverrideFields.All: return false;
                case ChurchArtOverrideFields.FlameSocket:
                case ChurchArtOverrideFields.FlameIntensity:
                case ChurchArtOverrideFields.FlameColor:
                case ChurchArtOverrideFields.SpillRadius:
                case ChurchArtOverrideFields.SpillIntensity: return definition.EmitsCandleLight;
                default: return true;
            }
        }

        static string FieldLabel(ChurchArtOverrideFields field)
        {
            switch (field)
            {
                case ChurchArtOverrideFields.Width: return "Width (world units)";
                case ChurchArtOverrideFields.Height: return "Upright height (world units)";
                case ChurchArtOverrideFields.Depth: return "Depth (world units)";
                case ChurchArtOverrideFields.MountHeight: return "Mount height (world units)";
                case ChurchArtOverrideFields.LocalOffset: return "Layout offset (world units)";
                case ChurchArtOverrideFields.RotationDegrees: return "Facing (degrees)";
                case ChurchArtOverrideFields.Contact: return "Ground contact (UV)";
                case ChurchArtOverrideFields.FlameSocket: return "Flame socket (UV)";
                case ChurchArtOverrideFields.SpillRadius: return "Spill radius (world units)";
                default: return ObjectNames.NicifyVariableName(field.ToString());
            }
        }

        static int FindSlot(ChurchInteriorDefinition owner, string slotId) =>
            owner.SlotOverrides.FindIndex(item => item != null && item.SlotId == slotId);

        // Copies the inherited value when opting in. Resetting the final field removes the row entirely.
        public static void SetOverride(ChurchInteriorDefinition owner, ChurchArtDefinition definition, string slotId,
            ChurchArtOverrideFields field, bool enable)
        {
            int index = FindSlot(owner, slotId);
            var row = index < 0 ? null : owner.SlotOverrides[index];
            bool currentlyOverridden = row != null && (row.OverrideFields & field) != 0;
            if (currentlyOverridden == enable) return;
            Undo.RecordObject(owner, enable ? "Override church art field" : "Reset church art field");
            if (enable)
            {
                if (row == null)
                {
                    row = new ChurchArtSlotOverride { SlotId = slotId };
                    owner.SlotOverrides.Add(row);
                }
                row.Values = ChurchArtValues.Merge(row.Values, definition.Defaults, field);
                row.OverrideFields |= field;
            }
            else
            {
                row.OverrideFields &= ~field;
                if (row.OverrideFields == ChurchArtOverrideFields.None) owner.SlotOverrides.RemoveAt(index);
            }
            EditorUtility.SetDirty(owner);
            ChurchArtEditorRefresh.Schedule();
        }

        void OnSceneGUI()
        {
            var part = (ChurchArtPlacement)target;
            if (part.Definition == null || part.Layout == null) return;
            var renderer = part.GetComponent<SpriteRenderer>();
            if (renderer == null) return;
            using (new Handles.DrawingScope(Color.cyan))
            {
                Handles.DrawWireCube(renderer.bounds.center, renderer.bounds.size);
                var contact = part.ContactPoint;
                Handles.Label(contact + Vector3.down * .13f, "Layout offset");
                EditorGUI.BeginChangeCheck();
                var moved = Handles.Slider2D(contact, Vector3.forward, Vector3.right, Vector3.up,
                    HandleUtility.GetHandleSize(contact) * .06f, Handles.RectangleHandleCap, Vector2.zero);
                if (EditorGUI.EndChangeCheck()) WritePoint(part, ChurchArtOverrideFields.LocalOffset, part.WorldToLocalOffset(moved));
            }
            if (part.Definition.EmitsCandleLight)
            {
                using (new Handles.DrawingScope(new Color(1f, .67f, .24f)))
                {
                    var flame = part.FlameWorldPoint;
                    Handles.DrawDottedLine(part.ContactPoint, flame, 3f);
                    Handles.Label(flame + Vector3.up * .13f, "Flame socket");
                    EditorGUI.BeginChangeCheck();
                    var moved = Handles.Slider2D(flame, Vector3.forward, Vector3.right, Vector3.up,
                        HandleUtility.GetHandleSize(flame) * .055f, Handles.CircleHandleCap, Vector2.zero);
                    if (EditorGUI.EndChangeCheck()) WritePoint(part, ChurchArtOverrideFields.FlameSocket, part.WorldToUv(moved));
                }
            }
        }

        void WritePoint(ChurchArtPlacement part, ChurchArtOverrideFields field, Vector2 point)
        {
            bool slot = SessionState.GetBool(ScopePreference, false);
            if (slot) SetOverride(part.Layout, part.Definition, part.SlotId, field, true);
            var owner = slot ? (UnityEngine.Object)part.Layout : part.Definition;
            using (var source = new SerializedObject(owner))
            {
                var values = slot ? source.FindProperty(nameof(ChurchInteriorDefinition.SlotOverrides))
                    .GetArrayElementAtIndex(FindSlot(part.Layout, part.SlotId)).FindPropertyRelative(nameof(ChurchArtSlotOverride.Values))
                    : source.FindProperty(nameof(ChurchArtDefinition.Defaults));
                values.FindPropertyRelative(field.ToString()).vector2Value = point;
                source.ApplyModifiedProperties();
            }
            ChurchArtEditorRefresh.Schedule();
        }
    }
}
