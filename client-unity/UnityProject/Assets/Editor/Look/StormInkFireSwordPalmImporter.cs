using System;
using System.Globalization;
using System.IO;
using Roguehalla.Client;
using UnityEditor;
using UnityEngine;

namespace Roguehalla.Editor
{
    /// <summary>Imports the measured per-frame hand sockets into Fire's sword authoring asset.</summary>
    public static class StormInkFireSwordPalmImporter
    {
        const string AssetPath = "Assets/Resources/StormInk/Actors/FireSwordAuthoring.asset";
        const string AtlasPath = "Assets/Resources/StormInk/Actors/FireSword.png";

        [MenuItem("Tools/RogueHalla/Storm Ink/Import Fire Sword Hand Coordinates")]
        static void Import()
        {
            string path = EditorUtility.OpenFilePanel("Fire sword hand coordinates", "", "csv");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                ImportCoordinates(path);
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Fire sword import failed", exception.Message, "OK");
            }
        }

        public static void ImportFromEnvironment()
        {
            string path = Environment.GetEnvironmentVariable("RH_FIRE_SWORD_PALMS_CSV");
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Set RH_FIRE_SWORD_PALMS_CSV to the measured hand table.");
            ImportCoordinates(path);
        }

        public static void ImportCoordinates(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("The Fire sword hand-coordinate CSV could not be read.", path);

            var coordinates = Read(path);
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            if (atlas == null || atlas.width % 10 != 0 || atlas.height % 8 != 0 || atlas.width / 10 != atlas.height / 8)
            {
                throw new InvalidOperationException("Import the 10-column, 8-row FireSword.png sheet before its hand table.");
            }

            var asset = AssetDatabase.LoadAssetAtPath<StormInkFireSwordAuthoring>(AssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<StormInkFireSwordAuthoring>();
                AssetDatabase.CreateAsset(asset, AssetPath);
            }

            Undo.RecordObject(asset, "Import Fire Sword Hand Coordinates");
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("atlas").objectReferenceValue = atlas;
            var poses = serialized.FindProperty("poses");
            if (poses.arraySize != 80) poses.arraySize = 80;
            for (int row = 0; row < 8; row++)
            for (int column = 0; column < 10; column++)
            {
                var pose = poses.GetArrayElementAtIndex(row * 10 + column);
                pose.FindPropertyRelative("Row").intValue = row;
                pose.FindPropertyRelative("Column").intValue = column;
            }

            var palms = serialized.FindProperty("palms");
            palms.arraySize = 80;
            for (int index = 0; index < coordinates.Length; index++)
            {
                var entry = palms.GetArrayElementAtIndex(index);
                WritePoint(entry.FindPropertyRelative("Right"), coordinates[index].RightX, coordinates[index].RightY);
                WritePoint(entry.FindPropertyRelative("Left"), coordinates[index].LeftX, coordinates[index].LeftY);
            }
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }

        readonly struct Coordinate
        {
            public readonly int RightX, RightY, LeftX, LeftY;
            public Coordinate(int rightX, int rightY, int leftX, int leftY)
            {
                RightX = rightX;
                RightY = rightY;
                LeftX = leftX;
                LeftY = leftY;
            }
        }

        static Coordinate[] Read(string path)
        {
            var lines = File.ReadAllLines(path);
            var values = new Coordinate[80];
            var seen = new bool[80];
            int first = lines.Length > 0 && lines[0].StartsWith("row,", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            for (int line = first; line < lines.Length; line++)
            {
                var fields = lines[line].Split(',');
                if (fields.Length != 6) throw new FormatException($"Expected six columns on CSV line {line + 1}.");
                int row = Parse(fields[0]), column = Parse(fields[1]);
                int rx = Parse(fields[2]), ry = Parse(fields[3]), lx = Parse(fields[4]), ly = Parse(fields[5]);
                if ((uint)row >= 8 || (uint)column >= 10 || (uint)rx >= 128 || (uint)ry >= 128 ||
                    (uint)lx >= 128 || (uint)ly >= 128)
                    throw new FormatException($"CSV line {line + 1} has an out-of-range row, column or pixel coordinate.");
                int index = row * 10 + column;
                if (seen[index]) throw new FormatException($"Facing {row}, pose {column} is listed more than once.");
                seen[index] = true;
                values[index] = new Coordinate(rx, ry, lx, ly);
            }
            if (Array.Exists(seen, present => !present)) throw new FormatException("The CSV must contain all 80 frame sockets.");
            return values;
        }

        static int Parse(string value) => int.Parse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);

        static void WritePoint(SerializedProperty property, int x, int y)
        {
            property.FindPropertyRelative("x").intValue = x;
            property.FindPropertyRelative("y").intValue = y;
        }
    }

    [CustomEditor(typeof(StormInkFireSwordAuthoring))]
    sealed class StormInkFireSwordAuthoringEditor : UnityEditor.Editor
    {
        static readonly string[] Rows = { "South", "South east", "East", "North east", "North", "North west", "West", "South west" };
        static readonly string[] Poses =
        {
            "A anticipation", "A passing", "A release", "A follow through", "A recovery",
            "B anticipation", "B passing", "B release", "B follow through", "B recovery"
        };
        readonly bool[] _expanded = new bool[8];

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("atlas"));
            var authoring = (StormInkFireSwordAuthoring)target;
            EditorGUILayout.LabelField("Shared Fire height", $"{authoring.WorldHeight:F2} world units");
            EditorGUILayout.LabelField("Shared Fire foot pivot", authoring.FootPivot.ToString("F3"));
            EditorGUILayout.HelpBox("Each cell adjusts the shared sword angle and draw depth for one painted hand pose. Fire uses the axe asset's height and foot anchor in every stance.", MessageType.Info);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("releaseATiming"), new GUIContent("Release A phase timing"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("releaseBTiming"), new GUIContent("Release B phase timing"));

            var poses = serializedObject.FindProperty("poses");
            var palms = serializedObject.FindProperty("palms");
            for (int row = 0; row < Rows.Length; row++)
            {
                _expanded[row] = EditorGUILayout.Foldout(_expanded[row], Rows[row], true);
                if (!_expanded[row]) continue;
                EditorGUI.indentLevel++;
                for (int column = 0; column < Poses.Length; column++)
                {
                    int index = row * 10 + column;
                    if (index >= poses.arraySize || index >= palms.arraySize) continue;
                    var pose = poses.GetArrayElementAtIndex(index);
                    var hands = palms.GetArrayElementAtIndex(index);
                    EditorGUILayout.LabelField(Poses[column], EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(hands.FindPropertyRelative("Right"), new GUIContent("Right hand socket"));
                    EditorGUILayout.PropertyField(hands.FindPropertyRelative("Left"), new GUIContent("Left hand socket"));
                    EditorGUILayout.PropertyField(pose.FindPropertyRelative("WeaponAngleOffset"), new GUIContent("Blade angle offset"));
                    EditorGUILayout.PropertyField(pose.FindPropertyRelative("WeaponDepthOffset"), new GUIContent("Weapon draw depth"));
                }
                EditorGUI.indentLevel--;
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
}
