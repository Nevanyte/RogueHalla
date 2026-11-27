// Builds the scene from code so the project needs no authored scene content: it runs
// in any (even empty) scene. Managed Web releases use pinned routes. Development pages may
// supply ?server=wss://host/arena/ws&trainingServer=wss://host/training/ws on their configured account origin.
using System;
using UnityEngine;
using Roguehalla.Net;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Roguehalla.Client
{
    /// <summary>Startup entry that builds the scene: a camera, then either a lab or preview stage chosen by a page query flag, or the Match (ArenaView, MatchClient, soundscape) configured from the query string. <c>Query</c> reads one flag.</summary>
    public static class Bootstrap
    {
        const string DefaultUrl = "ws://127.0.0.1:7777/ws";

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern string RhPage_GetSearch();
        [DllImport("__Internal")] static extern int RhPage_UseSplitBackend();
        static string Search() { return RhPage_GetSearch(); }
        static bool UseSplitBackend() => RhPage_UseSplitBackend() == 1;
#elif UNITY_EDITOR
        static string Search()
        {
            var query=UnityEditor.SessionState.GetString("Roguehalla.ArtPreviewQuery", "");
            UnityEditor.SessionState.EraseString("Roguehalla.ArtPreviewQuery");
            return query;
        }
#else
        static string Search() { return ""; }
#endif
#if !UNITY_WEBGL || UNITY_EDITOR
        static bool UseSplitBackend() => false;
#endif

        public static string Query(string search, string key, string fallback)
        {
            if (string.IsNullOrEmpty(search)) return fallback;
            foreach (var pair in search.TrimStart('?').Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0 && pair.Substring(0, eq) == key) return Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' '));
            }
            return fallback;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start()
        {
            string s = Search();
            var camGo = new GameObject("Camera");
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam.orthographic = true;
            cam.orthographicSize = 9f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(0x14, 0x17, 0x1b, 255);
            cam.allowHDR = true;
            cam.transform.position = new Vector3(24, -14, -30);
            cam.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderPostProcessing = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            camGo.tag = "MainCamera";

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            string uiReview = Query(s, "uiReview", "");
            if (uiReview == "hud" || uiReview == "planning" || uiReview == "map")
            {
                new GameObject("Offline UI layout review").AddComponent<UiLayoutReview>().Initialize(cam, uiReview);
                return;
            }
#endif

            // Serverless contrast lab (measurement stage for the character-contrast gate). Reachable only with ?contrastlab=1.
            if (ContrastLabStage.Requested(s))
            {
                new GameObject("Contrast lab").AddComponent<ContrastLabStage>().Initialize(cam, s);
                return;
            }

            // Serverless indicator gallery (the UI lab: synthetic view data through the real presenters). Reachable only with ?uilab=1.
            if (UiLabStage.Requested(s))
            {
                new GameObject("UI lab").AddComponent<UiLabStage>().Initialize(cam, s);
                return;
            }

            if (Query(s, "preview", "0") == "1")
            {
                new GameObject("Presentation study").AddComponent<ShowcasePreview>().Initialize(cam);
                return;
            }

            var go = new GameObject("Match");
            var arenaGo = new GameObject("Arena");
            var arena = arenaGo.AddComponent<ArenaView>();
            var mc = go.AddComponent<MatchClient>();
            mc.Arena = arena;
            mc.Cam = cam;
            go.AddComponent<ShowcaseSoundscape>().Initialize(mc);
            Uri.TryCreate(Application.absoluteURL, UriKind.Absolute, out var pageUri);
            ConfigureNetwork(mc, pageUri?.GetLeftPart(UriPartial.Path) ?? "", s, UseSplitBackend());
            mc.Profile = Query(s, "profile", PlayerPrefs.GetString("rh.ui.profile", ""));
            mc.DiagnosticRequested = Query(s, "diagnostic", "0") == "1";
            // Diagnostics only: ?pausepredict=0 turns the attack pause mirror off on a loopback page (including the practice page, which is
            // never "diagnostic"), which is how the browser measurement shows the old pull-back.
            mc.PausePredictionOff = pageUri != null && pageUri.IsLoopback && Query(s, "pausepredict", "1") == "0";
            mc.Token = mc.IsDiagnostic ? Query(s, "token", "") : "";
            mc.PendingSignInKey = mc.IsDiagnostic ? "" : UiBrowserStorage.ConsumeSignInKey();
            ulong hash; if (ulong.TryParse(Query(s, "hash", BakedContent.Hash.ToString(System.Globalization.CultureInfo.InvariantCulture)), out hash)) mc.ContentHash = hash;
            int faction; if (int.TryParse(Query(s, "faction", "4"), out faction)) mc.Faction = faction;
            // Synthetic joins require explicit loopback diagnostics. Ordinary profile/token/auto queries never bypass ownership.
            mc.AutoJoin = mc.IsDiagnostic && Query(s, "auto", "1") == "1";
            // Diagnostics only: a loopback page may ask for the read-only state mirror the UI scenario harness steers by.
            if (Query(s, "scenario.probe", "0") == "1" && pageUri != null && pageUri.IsLoopback) go.AddComponent<ScenarioProbe>().Initialize(mc);
        }

        static void ConfigureNetwork(MatchClient client, string pageUrl, string search, bool splitBackend)
        {
            client.PageUrl = pageUrl;
            client.SplitHostedBackend = splitBackend;
            if (AccountConnectionPolicy.TryGetHostedRoute(pageUrl, splitBackend, out _, out var hostedMatchUrl))
            {
                client.Url = hostedMatchUrl;
                client.TrainingUrl = "";
                client.AccountApi = "";
                return;
            }

            string defaultWs = DefaultUrl;
            try { defaultWs = AccountConnectionPolicy.DefaultMatchUrl(pageUrl); } catch (ArgumentException) { }
            client.Url = Query(search, "server", defaultWs);
            client.TrainingUrl = Query(search, "trainingServer", "");
            client.AccountApi = Query(search, "accountApi", Application.isEditor ? "http://127.0.0.1:7777/api" : "");
        }
    }
}
