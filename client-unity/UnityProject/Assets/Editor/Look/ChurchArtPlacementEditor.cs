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
            if (!editSlot)
            {
                var mounts = new Foldout { text = "Permitted orientations", value = false };
                void MountProperty(string name, string label)
                {
                    var property = defaults.FindProperty(name);
                    var editor = new PropertyField(property, label);
                    editor.BindProperty(property);
                    if (name == nameof(definition.AllowedTurns))
                        editor.RegisterCallback<SerializedPropertyChangeEvent>(_ => fields.schedule.Execute(RebindFields));
                    mounts.Add(editor);
                }
                MountProperty(nameof(definition.AllowedTurns), "Local turns");
                MountProperty(nameof(definition.ReflectPainting), "Reflect painting with layout");
                if (definition.Mount == ChurchArtMount.Wall) MountProperty(nameof(definition.PermittedBays), "Permitted wall bays");
                fields.Add(mounts);
            }
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
                var editor = FieldEditor(property, field, definition);
                editor.style.flexGrow = 1;
                editor.style.flexBasis = 0;
                editor.SetEnabled(!editSlot || overridden);
                line.Add(editor);
                if (!editSlot && overridden)
                    line.Add(new Label("Slot override") { tooltip = "This field has its own value in the selected layout slot." });
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

        static VisualElement FieldEditor(SerializedProperty property, ChurchArtOverrideFields field, ChurchArtDefinition definition)
        {
            if (field == ChurchArtOverrideFields.RotationDegrees && definition.AllowedTurns != ChurchArtTurns.AnyAngle)
            {
                int step = definition.AllowedTurns == ChurchArtTurns.ReverseOnly ? 180 : 90;
                var choices = new System.Collections.Generic.List<string>();
                for (int degrees = 0; degrees < 360; degrees += step) choices.Add(degrees + "°");
                int index = Mathf.RoundToInt(Mathf.Repeat(definition.LocalTurn(property.floatValue), 360f) / step) % choices.Count;
                var menu = new DropdownField("Local facing", choices, index);
                menu.RegisterValueChangedCallback(change =>
                {
                    property.serializedObject.Update();
                    property.floatValue = choices.IndexOf(change.newValue) * step;
                    property.serializedObject.ApplyModifiedProperties();
                    ChurchArtEditorRefresh.Schedule();
                });
                return menu;
            }
            var editor = new PropertyField(property, FieldLabel(field, definition));
            editor.BindProperty(property);
            return editor;
        }

        static bool Visible(ChurchArtOverrideFields field, ChurchArtDefinition definition)
        {
            switch (field)
            {
                case ChurchArtOverrideFields.None:
                case ChurchArtOverrideFields.All: return false;
                case ChurchArtOverrideFields.Depth: return definition.Mount != ChurchArtMount.Upright;
                case ChurchArtOverrideFields.Height: return definition.Mount != ChurchArtMount.Floor;
                case ChurchArtOverrideFields.RotationDegrees: return definition.AllowedTurns != ChurchArtTurns.FixedToSlot;
                case ChurchArtOverrideFields.PaneMode: return definition.Mount == ChurchArtMount.Wall;
                case ChurchArtOverrideFields.FlameLayer:
                case ChurchArtOverrideFields.FlameLayerSize: return definition.EmitsCandleLight;
                case ChurchArtOverrideFields.FlameSocket:
                case ChurchArtOverrideFields.FlameIntensity:
                case ChurchArtOverrideFields.FlameColor:
                case ChurchArtOverrideFields.SpillRadius:
                case ChurchArtOverrideFields.SpillIntensity: return definition.EmitsCandleLight || definition.EmitsWindowLight;
                default: return true;
            }
        }

        static string FieldLabel(ChurchArtOverrideFields field, ChurchArtDefinition definition)
        {
            if (definition.Mount == ChurchArtMount.Wall)
            {
                if (field == ChurchArtOverrideFields.Depth) return "Jamb thickness (world units)";
                if (field == ChurchArtOverrideFields.FlameSocket) return "Pane light source (UV)";
                if (field == ChurchArtOverrideFields.FlameColor) return "Pane light color";
                if (field == ChurchArtOverrideFields.FlameIntensity) return "Pane light intensity";
            }
            switch (field)
            {
                case ChurchArtOverrideFields.PaneMode: return "Window reveal";
                case ChurchArtOverrideFields.FlameLayer: return "Painted flame";
                case ChurchArtOverrideFields.FlameLayerSize: return "Flame size (world units)";
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
            bool editLocal = SessionState.GetBool(ScopePreference, false);
            int index = FindSlot(part.Layout, part.SlotId);
            var overrides = index < 0 ? ChurchArtOverrideFields.None : part.Layout.SlotOverrides[index].OverrideFields;
            bool CanMove(ChurchArtOverrideFields field) => editLocal || (overrides & field) == 0;

            void HandlePoint(Vector3 origin, ChurchArtOverrideFields field, string label, Handles.CapFunction cap,
                Func<Vector3, Vector2> toValue)
            {
                bool active = CanMove(field);
                Handles.Label(origin + Vector3.up * .13f, active ? label : label + " (overridden in This slot)");
                if (!active) return;
                EditorGUI.BeginChangeCheck();
                var moved = Handles.Slider2D(origin, Vector3.forward, Vector3.right, Vector3.up,
                    HandleUtility.GetHandleSize(origin) * .055f, cap, Vector2.zero);
                if (EditorGUI.EndChangeCheck()) WritePoint(part, field, toValue(moved));
            }

            using (new Handles.DrawingScope(Color.cyan))
            {
                Handles.DrawWireCube(renderer.bounds.center, renderer.bounds.size);
                HandlePoint(part.ContactPoint, ChurchArtOverrideFields.LocalOffset, "Layout offset",
                    Handles.RectangleHandleCap, part.WorldToLocalOffset);
            }
            if (part.Definition.EmitsCandleLight || part.Definition.EmitsWindowLight)
            {
                using (new Handles.DrawingScope(new Color(1f, .67f, .24f)))
                {
                    var wick = part.FlameWickPoint;
                    Handles.DrawDottedLine(part.ContactPoint, wick, 3f);
                    HandlePoint(wick, ChurchArtOverrideFields.FlameSocket, part.Definition.EmitsWindowLight ? "Pane light" : "Flame socket",
                        Handles.CircleHandleCap, part.WorldToUv);
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
