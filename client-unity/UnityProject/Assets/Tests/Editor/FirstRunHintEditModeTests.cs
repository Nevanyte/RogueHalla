using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-FIRST-RUN-HINT-1: the first-run hint stands from the first snapshot, is counted in seconds of live play (not wall time,
    /// not lobby, planning, a held Tab map or a pending launch), survives the join's resets and a clock cut, ends for good at 20
    /// seconds, on a result, an ended phase or practice, and leaves a resume status alone. A real MatchClient on a fake transport
    /// whose session is reset the way a real join resets it; time moves only through <see cref="MatchHud.NoticeDtOverride"/>.
    /// </summary>
    public sealed class FirstRunHintEditModeTests
    {
        sealed class Transport : ITransport
        {
            public event Action Opened;
            public event Action<byte[]> Received;
            public event Action<string> Closed;
            public void Connect(string url) { }
            public void Send(byte[] frame) { }
            public void Close() => Closed?.Invoke("closed");
            public void Poll() { }
            public void Open() => Opened?.Invoke();
            public void Deliver(ServerMsg message) => Received?.Invoke(message.Encode());
        }

        sealed class Accounts : IAccountTransport
        {
            readonly long _runs;
            public Accounts(long runs) { _runs = runs; }
            public Task<AccountHttpResponse> GetProfileAsync(string url, string ownerToken, string passcode, CancellationToken cancellation) =>
                Task.FromResult(new AccountHttpResponse(200, new AccountReply
                {
                    profile = new AccountProfile { name = "alice", gv = 500, gvLifetime = 1000,
                        stats = new AccountStats { runs = _runs, wins = 0, draws = 0, deaths = 0, kills = 0 } },
                    token = new string('a', 64), created = true, FullProfile = Profile("alice", _runs)
                }));
        }

        sealed class Credentials : IAccountCredentialStore
        {
            public string Read(string endpointScope, string normalizedName) => null;
            public bool WriteConfirmed(string endpointScope, string normalizedName, string token) => false;
        }

        static AccountFullProfile Profile(string name, long runs) => new AccountFullProfile(name, 500, 1000,
            new Dictionary<string, long>(), new AccountFullStats(runs, 0, 0, 0, 0),
            new AccountFullCosmetics(new string[0], new Dictionary<string, string>()),
            new Dictionary<string, long>(), new AccountFullProgress(0, 0, 0, new Dictionary<string, long>(), new Dictionary<string, long>()));

        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        const string Hint = "WASD or right-click move · Left-click attack · H help";
        const string SaveFailure = "Run recovery is unavailable in this browser. Keep this page open.";

        GameObject _root, _events;
        EventSystem _priorEvents;
        MatchClient _client;
        MatchHud _hud;
        Session _session;
        Transport _transport;
        Action _reset;
        bool? _priorReduced;
        uint _tick;
        double _ms = 50;

        [SetUp]
        public void SetUp()
        {
            _priorEvents = EventSystem.current;
            _priorReduced = UiMotion.ReducedOverride;
            UiMotion.ReducedOverride = false;
            MatchHud.NoticeDtOverride = .25f;
            _events = new GameObject("First run hint events", typeof(UiInputRoutingEventSystem));
            var system = _events.GetComponent<UiInputRoutingEventSystem>(); system.enabled = false; system.enabled = true;
            EventSystem.current = system;
            _root = new GameObject("First run hint fixture");
            _client = _root.AddComponent<MatchClient>(); _client.enabled = false;
            _transport = new Transport();
            _session = new Session(_transport, new SessionOptions { Url = "ws://hint/ws", Profile = "hint", Now = () => 1000 });
            typeof(MatchClient).GetMethod("InitializeLayoutReview", Any).Invoke(_client, new object[] { _session });
            _hud = _client.Hud;
            // A real join: BindPresentationSession resets the HUD, and the session resets it at the Welcome and at every clock cut.
            _hud.ResetTransientPresentation();
            _reset = () => _hud.ResetTransientPresentation();
            _session.TimelineReset += _reset;
            _session.Connect(); _transport.Open();
            _transport.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = new byte[16], Session = new byte[16],
                PlayerId = 1, TickRate = 20, ServerMs = 50, Map = new MapBlob(32, 32, new byte[1024]) });
            Snap(0);
            Assert.That((bool)Get("_firstRunHint"), Is.True, "rig: Bind armed the first-run hint and the resets left it armed");
            var settings = (ShowcaseSettings)Get("_showcaseSettings");
            Assert.That(settings, Is.Not.Null, "rig: the HUD built its settings component");
            if (ShowcaseSettings.Current == null) typeof(ShowcaseSettings).GetMethod("Awake", Any).Invoke(settings, null);
            float factor = HudLayout.CanvasScalerFactor(1422, 800);
            ((HudStage)Get("_stageScaler")).Apply(1422, 800, factor);
            ((HudStage)Get("_chromeScaler")).Apply(1422, 800, factor);
        }

        [TearDown]
        public void TearDown()
        {
            MatchHud.NoticeDtOverride = null;
            UiMotion.ReducedOverride = _priorReduced;
            if (_session != null && _reset != null) _session.TimelineReset -= _reset;
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
            if (_events != null) UnityEngine.Object.DestroyImmediate(_events);
            if (_priorEvents != null) EventSystem.current = _priorEvents;
            // SCRATCH-TEARDOWN
        }

        // ---- helpers ------------------------------------------------------------------------------------------------------------

        object Get(string field) => typeof(MatchHud).GetField(field, Any).GetValue(_hud);
        void Set(string field, object value) => typeof(MatchHud).GetField(field, Any).SetValue(_hud, value);

        /// <summary>A snapshot of the given wire phase: tick and server time both rise, so the session does not take it for a clock cut.</summary>
        void Snap(byte phase)
        {
            _ms += 50;
            Deliver(phase);
        }

        /// <summary>A higher tick with a lower server time: Session takes it for a source-clock cut (TimelineReset).</summary>
        void SnapCut()
        {
            _ms = 20;
            Deliver(1);
        }

        void Deliver(byte phase) =>
            _transport.Deliver(new ServerMsg.Snapshot { Tick = ++_tick, ServerTimeOverrideMs = _ms, Phase = phase,
                Me = new WireSelf { Id = 1, X = 64, Y = 64, Hp = 100, MaxHp = 100, Status = 1, Weapon = 0 } });

        /// <summary>One player-loop frame of the HUD: dirty flag set, LateUpdate run, the notice clock advanced by dt.</summary>
        void Frame(float dt = .25f)
        {
            MatchHud.NoticeDtOverride = dt;
            Set("_dirty", true);
            typeof(MatchHud).GetMethod("LateUpdate", Any).Invoke(_hud, null);
        }

        /// <summary>A frame in which nothing is dirty: the dirty path of HudLateUpdate is skipped.</summary>
        void Quiet(float dt = .25f)
        {
            MatchHud.NoticeDtOverride = dt;
            typeof(MatchHud).GetMethod("LateUpdate", Any).Invoke(_hud, null);
        }

        void Frames(int count) { for (int i = 0; i < count; i++) Frame(); }
        void Seconds(float seconds) => Frames((int)Math.Round(seconds / .25f));

        static List<RectTransform> Pills(MatchHud hud)
        {
            var stage = (RectTransform)typeof(MatchHud).GetField("_stage", Any).GetValue(hud);
            var lane = stage.Find("FeedbackLane");
            if (lane == null) throw new InvalidOperationException("no FeedbackLane under the HUD stage");
            var list = new List<RectTransform>();
            foreach (Transform child in lane)
                if (child.name.StartsWith("Toast") && child.gameObject.activeInHierarchy) list.Add((RectTransform)child);
            return list;
        }

        static string PillText(RectTransform pill)
        {
            var t = pill.Find("Text");
            if (t == null) throw new InvalidOperationException(pill.name + " has no child 'Text'");
            return t.GetComponent<TMP_Text>().text;
        }

        int PillCount() => Pills(_hud).Count;
        string FirstPillText() { var pills = Pills(_hud); return pills.Count == 0 ? null : PillText(pills[0]); }
        string Feedback() => (string)Get("_feedback");
        string Sticky() => (string)Get("_stickyNotice");
        bool Armed() => (bool)Get("_firstRunHint");

        // ---- rig A tests --------------------------------------------------------------------------------------------------------

        [Test]
        public void TheHintIsOnTheLaneInTheLobby()
        {
            Frames(2);
            Assert.That(PillCount(), Is.EqualTo(1), "one pill in the feedback lane after the join's resets");
            Assert.That(FirstPillText(), Is.EqualTo(Hint), "the pill is the hint");
        }

        [Test]
        public void LobbyAndPlanningSecondsAreNotCounted()
        {
            Seconds(30);
            Assert.That(PillCount(), Is.EqualTo(1), "30 lobby seconds: the hint stands");
            Assert.That(Armed(), Is.True, "30 lobby seconds count nothing");
            Snap(3);
            Seconds(30);
            Assert.That(PillCount(), Is.EqualTo(1), "30 planning seconds: the hint stands (no Plan is delivered, so the lane stays visible)");
            Assert.That(Armed(), Is.True, "30 planning seconds count nothing");
        }

        [Test]
        public void TwentySecondsOfPlayAndNoMore()
        {
            Seconds(30);
            Snap(1);
            Seconds(19);
            Assert.That(PillCount(), Is.EqualTo(1), "19 seconds of play: the pill stands");
            Seconds(2);
            Assert.That(PillCount(), Is.EqualTo(0), "21 seconds of play: no pill");
            Assert.That(Armed(), Is.False, "the hint is spent");
            Assert.That(Feedback(), Is.EqualTo(""), "the feedback line is empty");
            Assert.That(Sticky(), Is.EqualTo(""), "the lane's sticky line is empty");
            Seconds(10);
            Assert.That(PillCount(), Is.EqualTo(0), "still none ten seconds later");
        }

        [Test]
        public void OnlyVisibleSecondsCount()
        {
            Snap(1);
            Seconds(5);
            _hud.TacticalMap.SetOpen(true);
            Assert.That(_hud.TacticalMap.IsOpen, Is.True, "premise: the Tab map is open");
            Seconds(10);
            Assert.That(PillCount(), Is.EqualTo(0), "the lane is hidden while the map is open");
            _hud.TacticalMap.SetOpen(false);
            Seconds(14);
            Assert.That(PillCount(), Is.EqualTo(1), "19 visible seconds: the hint stands");
            Seconds(2);
            Assert.That(PillCount(), Is.EqualTo(0), "21 visible seconds: gone");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AClockCutNeitherWipesNorRestartsTheHint(bool realCut)
        {
            Snap(1);
            Seconds(5);
            if (!realCut) _hud.ResetTransientPresentation();
            else
            {
                uint before = _session.TimelineEpoch;
                SnapCut();
                Assert.That(_session.TimelineEpoch, Is.EqualTo(before + 1), "premise: the snapshot was a source cut (the rig's handler ran the reset)");
            }
            Frames(2);
            Assert.That(PillCount(), Is.EqualTo(1), "the hint is back after the reset");
            Seconds(14);
            Assert.That(PillCount(), Is.EqualTo(1), "19.5 seconds in total: not restarted, not spent");
            Seconds(1);
            Assert.That(PillCount(), Is.EqualTo(0), "20.5 seconds: gone");
        }

        [Test]
        public void AMatchThatEndsInsideTwentySecondsLeavesNoHint()
        {
            Snap(1);
            Seconds(10);
            Assert.That(PillCount(), Is.EqualTo(1), "premise: the hint stands after 10 seconds");
            _transport.Deliver(new ServerMsg.Events { Tick = ++_tick, List = { new WireEvent { Kind = EventKind.Result,
                Result = new WireResult { Outcome = 2, Place = 1, Players = 1, KilledBy = "x", DurationSec = 8 } } } });
            Assert.That(_session.LastResult, Is.Not.Null, "premise: the session holds a result");
            Frames(3);
            Assert.That(Feedback(), Is.EqualTo(""), "the feedback line is empty");
            Assert.That(Sticky(), Is.EqualTo(""), "the sticky line is empty");
            Assert.That(Armed(), Is.False, "the hint is over for good");
        }

        [Test]
        public void APhaseEndedSnapshotEndsItForGood()
        {
            Snap(1);
            Seconds(10);
            Assert.That(PillCount(), Is.EqualTo(1), "premise: the hint stands after 10 seconds");
            Snap(2);
            Frames(3);
            Assert.That(PillCount(), Is.EqualTo(0), "an Ended phase removes the pill");
            Assert.That(Armed(), Is.False, "and the hint");
            Snap(1);
            Seconds(5);
            Assert.That(PillCount(), Is.EqualTo(0), "a later Active snapshot does not revive it");
            Assert.That(Armed(), Is.False, "still over");
        }

        [Test]
        public void ARebindArmsItAgain()
        {
            Snap(1);
            Seconds(21);
            Assert.That(PillCount(), Is.EqualTo(0), "premise: the hint is spent");
            _hud.Bind(_session);
            _hud.ResetTransientPresentation();
            Frames(2);
            Assert.That(PillCount(), Is.EqualTo(1), "a rebind arms it again");
            Seconds(19);
            Assert.That(PillCount(), Is.EqualTo(1), "and the count restarted from 0 (19.5 seconds)");
            Seconds(2);
            Assert.That(PillCount(), Is.EqualTo(0), "gone at 20");
        }

        [TestCase(0L, true)]
        [TestCase(1L, true)]
        [TestCase(2L, false)]
        public async Task ProfileRunsDecideWhetherTheHintIsOwed(long runs, bool owed)
        {
            var ownerObject = new GameObject("First run hint account owner");
            ownerObject.transform.SetParent(_root.transform, false);
            var owner = ownerObject.AddComponent<AccountCoordinator>();
            owner.Initialize(AccountEndpoint.FromPage("https://local-test.invalid/"), new Accounts(runs), new Credentials());
            typeof(MatchClient).GetField("<AccountOwner>k__BackingField", Any).SetValue(_client, owner);
            bool opened = await owner.Flow.OpenProfileAsync("alice");
            Assert.That(opened, Is.True, "premise: the profile opened");
            Assert.That(_client.AccountFlow.Snapshot.FullProfile.Stats.Runs, Is.EqualTo(runs), "premise: the client sees the profile's runs");
            _hud.Bind(_session);
            _hud.ResetTransientPresentation();
            Snap(1);
            Frames(2);
            Assert.That(PillCount(), Is.EqualTo(owed ? 1 : 0), owed ? "fewer than two runs: the hint shows" : "two runs: no hint");
            Assert.That(Armed(), Is.EqualTo(owed), "armed only when owed");
        }

        [Test]
        public void ASteadyTickAllocatesNothing()
        {
            Snap(1);
            Frames(2);
            var method = typeof(MatchHud).GetMethod("TickFirstRunHint", Any);
            Assert.That(method, Is.Not.Null, "TickFirstRunHint keeps its name");
            var tick = (Action)method.CreateDelegate(typeof(Action), _hud);
            MatchHud.NoticeDtOverride = 0f;
            AllocationMeter.AssertNone(tick, 200, "a steady hint tick");
        }

        [Test]
        public void QuietFramesCountToo()
        {
            Snap(1);
            Quiet();
            Assert.That((bool)Get("_dirty"), Is.False, "premise: these are not dirty frames");
            Quiet();
            Assert.That(PillCount(), Is.EqualTo(1), "the hint is on the lane after two frames (the lane is presented a frame after the tick puts the line back)");
            for (int i = 2; i < 79; i++) Quiet();
            Assert.That(PillCount(), Is.EqualTo(1), "79 quiet frames (19.75 s): the hint stands");
            Assert.That(Armed(), Is.True, "not yet spent");
            Quiet();
            Assert.That(Armed(), Is.False, "the 80th quiet frame (20.00 s) ends it");
            Assert.That(Feedback(), Is.EqualTo(""), "and empties the line on that frame");
            Quiet();
            Assert.That(PillCount(), Is.EqualTo(0), "one frame later the pill is gone");
        }

        [Test]
        public void NothingIsRestoredWhileTheLaunchIsPending()
        {
            var config = new MatchLaunchConfig(AccountEndpoint.FromPage("https://x.example/play"), "wss://x.example/arena/ws", "wss://x.example/training/ws");
            var field = typeof(MatchClient).GetField("_sessionTarget", Any);
            field.SetValue(_client, config.Target(MatchLaunchMode.Arena));
            Assert.That(_client.LaunchPending, Is.True, "premise: the launch is pending (the session is in the match, the mode is not confirmed)");
            _hud.ResetTransientPresentation();
            for (int i = 0; i < 4; i++) Frame();
            Assert.That(Feedback(), Is.EqualTo(""), "nothing is restored while pending");
            Assert.That(PillCount(), Is.EqualTo(0), "no pill while pending");
            Assert.That(Armed(), Is.True, "still armed");
            Snap(1);
            Seconds(30);
            Assert.That(Feedback(), Is.EqualTo(""), "still empty after 30 pending seconds");
            Assert.That(PillCount(), Is.EqualTo(0), "still no pill");
            Assert.That(Armed(), Is.True, "a count that ran while pending would have ended it");
            field.SetValue(_client, null);
            Frames(2);
            Assert.That(PillCount(), Is.EqualTo(1), "the launch is no longer pending: the hint is restored");
            Seconds(19);
            Assert.That(PillCount(), Is.EqualTo(1), "and counted from 0 (19.5 seconds)");
            Seconds(2);
            Assert.That(PillCount(), Is.EqualTo(0), "gone");
        }

        [Test]
        public void AFeedbackLineThatIsNotTheHintSurvives()
        {
            Snap(1);
            Frames(2);
            Assert.That(PillCount(), Is.EqualTo(1), "premise: the hint stands");
            Set("_feedback", SaveFailure);
            Frames(2);
            Assert.That(PillCount(), Is.EqualTo(1), "one pill");
            Assert.That(FirstPillText(), Is.EqualTo(SaveFailure), "with the status text");
            Seconds(21);
            Assert.That(PillCount(), Is.EqualTo(1), "21 seconds later the status is still there");
            Assert.That(FirstPillText(), Is.EqualTo(SaveFailure), "with its text");
            Assert.That(Armed(), Is.False, "the count ended");
            Assert.That(Feedback(), Is.EqualTo(SaveFailure), "and left the line alone");
        }

        // ---- real joins (TrainingHudRig) ----------------------------------------------------------------------------------------

        static int RigPills(TrainingHudRig rig, string text)
        {
            int n = 0;
            foreach (var pill in Pills(rig.Hud)) if (text == null || PillText(pill) == text) n++;
            return n;
        }

        /// <summary>
        /// The rig's client has no seat storage, so its first confirmed snapshot writes the resume status "This run could not be saved
        /// for reload." and the HUD copies it into the feedback line (an error outranks the hint: test 15). Clear both, as a browser
        /// that can save would leave them, so these tests see the hint and not the status.
        /// </summary>
        static void ClearSaveStatus(TrainingHudRig rig)
        {
            typeof(MatchClient).GetField("_resumeStatus", Any).SetValue(rig.Client, "");
            typeof(MatchHud).GetField("_feedback", Any).SetValue(rig.Hud, "");
        }

        static string RigFeedback(TrainingHudRig rig) => (string)TrainingHudRig.GetField(rig.Hud, "_feedback");
        static bool RigArmed(TrainingHudRig rig) => (bool)TrainingHudRig.GetField(rig.Hud, "_firstRunHint");

        [Test]
        public async Task InPracticeTheHintNeverShows()
        {
            var priorReduced = UiMotion.ReducedOverride;
            UiMotion.ReducedOverride = false;
            MatchHud.NoticeDtOverride = 0f;
            try
            {
                using (var rig = await TrainingHudRig.Start(practice: true))
                {
                    ClearSaveStatus(rig);
                    MatchHud.NoticeDtOverride = .25f;
                    for (int i = 0; i < 4; i++) rig.Frame();
                    Assert.That(RigPills(rig, Hint), Is.EqualTo(0), "no hint pill in practice");
                    Assert.That(RigArmed(rig), Is.False, "the hint is off in practice");
                    Assert.That(RigFeedback(rig), Is.EqualTo(""), "the feedback line is empty");
                    for (int i = 0; i < 100; i++) rig.Frame();
                    Assert.That(RigPills(rig, Hint), Is.EqualTo(0), "no hint pill later either");
                    Assert.That(RigArmed(rig), Is.False, "still off");
                    Assert.That(RigFeedback(rig), Is.EqualTo(""), "still empty");
                }
            }
            finally { MatchHud.NoticeDtOverride = null; UiMotion.ReducedOverride = priorReduced; }
        }

        [Test]
        public async Task AfterARealJoinTheHintStandsThenGoes()
        {
            var priorReduced = UiMotion.ReducedOverride;
            UiMotion.ReducedOverride = false;
            MatchHud.NoticeDtOverride = 0f;
            try
            {
                using (var rig = await TrainingHudRig.Start(practice: false))
                {
                    var visible = typeof(MatchHud).GetProperty("WorldHudVisible", Any);
                    Assert.That(visible, Is.Not.Null, "premise: WorldHudVisible exists");
                    Assert.That((bool)visible.GetValue(rig.Hud), Is.True, "premise: the world HUD is visible");
                    ClearSaveStatus(rig);
                    rig.Frame(); rig.Frame();
                    Assert.That(RigPills(rig, null), Is.EqualTo(1), "after a real join the hint is on the lane");
                    Assert.That(RigPills(rig, Hint), Is.EqualTo(1), "and it is the hint");
                    MatchHud.NoticeDtOverride = .25f;
                    for (int i = 0; i < 76; i++) rig.Frame();
                    Assert.That(RigPills(rig, Hint), Is.EqualTo(1), "19 seconds of play: it stands");
                    for (int i = 0; i < 8; i++) rig.Frame();
                    Assert.That(RigPills(rig, null), Is.EqualTo(0), "21 seconds of play: no pill");
                    Assert.That(RigArmed(rig), Is.False, "spent");
                }
            }
            finally { MatchHud.NoticeDtOverride = null; UiMotion.ReducedOverride = priorReduced; }
        }
    }
}
