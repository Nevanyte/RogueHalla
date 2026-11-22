using System;
using Roguehalla.Client;
using UnityEditor;
using UnityEngine;

namespace Roguehalla.Editor
{
    [CustomEditor(typeof(ColiseumParapetArt))]
    sealed class ColiseumParapetInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (DrawDefaultInspector()) ColiseumParapetRefresh.Schedule();
        }
    }

    [InitializeOnLoad]
    sealed class ColiseumParapetRefresh : AssetPostprocessor
    {
        static string sourcePath;
        static ColiseumParapetRefresh() => Undo.undoRedoPerformed += Schedule;

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (!RegionWallAtlas.HasParapetAtlas) return;
            var art = RegionWallAtlas.ParapetSource;
            string definition = AssetDatabase.GetAssetPath(art);
            string pigment = art != null ? AssetDatabase.GetAssetPath(art.Pigment) : null;
            bool AffectsArt(string path) => path == definition || path == pigment || path == sourcePath ||
                path.EndsWith("/Resources/StormInk/Coliseum/ColiseumParapetArt.asset", StringComparison.Ordinal);
            if (Array.Exists(imported, AffectsArt) || Array.Exists(deleted, AffectsArt) ||
                Array.Exists(moved, AffectsArt) || Array.Exists(movedFrom, AffectsArt)) Schedule();
            sourcePath = pigment;
        }

        internal static void Schedule()
        {
            if (!RegionWallAtlas.HasParapetAtlas) return;
            EditorApplication.delayCall -= Apply;
            EditorApplication.delayCall += Apply;
        }

        static void Apply()
        {
            RegionWallAtlas.RefreshParapetFinish();
            SceneView.RepaintAll();
        }
    }
}
