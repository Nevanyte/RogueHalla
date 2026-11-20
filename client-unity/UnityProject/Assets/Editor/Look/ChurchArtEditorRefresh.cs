using System;
using Roguehalla.Client;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Roguehalla.Editor
{
    /// <summary>Refreshes generated church art and the studio cast after an artist edits, undoes or reimports a source.</summary>
    sealed class ChurchArtReimport : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (!(StageUtility.GetCurrentStage() is StormInkChurchStage)) return;
            var masonry = ChurchMasonryArt.Load();
            var wash = masonry != null ? AssetDatabase.GetAssetPath(masonry.Wash) : null;
            var carving = masonry != null ? AssetDatabase.GetAssetPath(masonry.CarvedFace) : null;
            foreach (var path in imported)
                if (path == wash || path == carving) { ChurchArtEditorRefresh.ScheduleMasonry(); break; }
            foreach (var path in imported)
                if (path.IndexOf("/Resources/StormInk/", StringComparison.Ordinal) >= 0)
                {
                    ChurchArtEditorRefresh.Schedule();
                    return;
                }
        }
    }

    [InitializeOnLoad]
    static class ChurchArtEditorRefresh
    {
        static ChurchArtEditorRefresh()
        {
            ObjectChangeEvents.changesPublished += OnChanges;
            Undo.undoRedoPerformed += Schedule;
        }

        static bool IsCastSource(UnityEngine.Object source) =>
            source is StormInkEquipmentRegistration || source is StormInkFireAxeAuthoring ||
            source is StormInkFireSwordAuthoring || source is StormInkFireBowAuthoring;

        static void OnChanges(ref ObjectChangeEventStream stream)
        {
            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.ChangeAssetObjectProperties) continue;
                stream.GetChangeAssetObjectPropertiesEvent(i, out var change);
                var asset = EditorUtility.InstanceIDToObject(change.instanceId);
                if (asset is ChurchMasonryArt) ScheduleMasonry();
                if (asset is ChurchArtDefinition || asset is ChurchInteriorDefinition ||
                    IsCastSource(asset))
                {
                    Schedule();
                    return;
                }
            }
        }

        internal static void ScheduleMasonry()
        {
            EditorApplication.delayCall -= RefreshMasonry;
            EditorApplication.delayCall += RefreshMasonry;
        }

        static void RefreshMasonry()
        {
            if (StageUtility.GetCurrentStage() is StormInkChurchStage stage) stage.RefreshMasonry();
            else RegionWallAtlas.RefreshNaveFinish();
        }

        internal static void Schedule()
        {
            EditorApplication.delayCall -= Refresh;
            EditorApplication.delayCall += Refresh;
        }

        static void Refresh()
        {
            // Includes isolated preview scenes, which Object.FindObjectsByType omits.
            foreach (var part in Resources.FindObjectsOfTypeAll<ChurchArtPlacement>())
                if (part.gameObject.scene.IsValid() && part.Definition != null && part.Layout != null)
                    part.RefreshResolved();
            if (StageUtility.GetCurrentStage() is StormInkChurchStage stage) stage.RefreshCast();
            SceneView.RepaintAll();
        }
    }
}
