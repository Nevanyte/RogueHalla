using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Roguehalla.Client;
using Roguehalla.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Roguehalla.Net.Tests
{
    public sealed class HubProgressViewEditModeTests
    {
        sealed class ProgressTransport : IAccountTransport, IAccountApiTransport
        {
            public int ProfileReads;
            public int PublicBoardReads;
            public int OwnerBoardReads;
            public readonly TaskCompletionSource<AccountApiResponse> LateOwnerBoard =
                new TaskCompletionSource<AccountApiResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            readonly AccountLeaderboard _publicBoard = Board(null);
            readonly AccountLeaderboard _ownerBoard = Board(new AccountLeaderboardRank(777, 654321));

            public Task<AccountHttpResponse> GetProfileAsync(string url, string ownerToken, string passcode, CancellationToken cancellation)
            {
                ProfileReads++;
                return Task.FromResult(new AccountHttpResponse(200, new AccountReply
                {
                    profile = new AccountProfile { name = "alice", gv = 500, gvLifetime = 1000,
                        stats = new AccountStats { runs = 0, wins = 0, draws = 0, deaths = 0, kills = 0 } },
                    token = new string('a', 64), created = true, FullProfile = Profile("alice")
                }));
            }

            public Task<AccountApiResponse> SendAsync(AccountApiRequest request, CancellationToken cancellation)
            {
                if (!request.Url.Contains("leaderboard", System.StringComparison.Ordinal))
                    return Task.FromResult(new AccountApiResponse(404));
                if (request.Url.Contains("&name=", System.StringComparison.Ordinal))
                {
                    OwnerBoardReads++;
                    return OwnerBoardReads == 2 ? LateOwnerBoard.Task :
                        Task.FromResult(new AccountApiResponse(200, leaderboard: _ownerBoard));
                }
                PublicBoardReads++;
                return Task.FromResult(new AccountApiResponse(200, leaderboard: _publicBoard));
            }
        }

        static AccountFullProfile Profile(IDictionary<string, long> achievements = null)
        {
            return new AccountFullProfile("Ada", long.MaxValue, long.MaxValue,
                new Dictionary<string, long> { ["2026-11"] = long.MaxValue },
                new AccountFullStats(long.MaxValue, 3, 4, 5, 6),
                new AccountFullCosmetics(new string[0], new Dictionary<string, string>()),
                achievements ?? new Dictionary<string, long>(),
                new AccountFullProgress(1, 2, 3, new Dictionary<string, long> { ["FutureFaction"] = long.MaxValue }, new Dictionary<string, long>()));
        }

        [Test]
        public void FormatsInt64WithoutFloatingPointLoss()
        {
            Assert.AreEqual("9,223,372,036,854,775,807", HubProgressView.FormatCount(long.MaxValue));
            Assert.AreEqual("9,223,372,036,854,775,807", HubProgressView.FormatCount(Profile().Gv));
        }

        [Test]
        public void LeaderboardRowsKeepEvenSpacingAtNarrowViewportWithLongNames()
        {
            var root = new GameObject("Leaderboard spacing fixture", typeof(RectTransform), typeof(Canvas));
            try
            {
                // 960x600 at HudLayout's .8 scale is a 1200x750 HUD-unit canvas.
                root.GetComponent<RectTransform>().sizeDelta = new Vector2(1200, 750);
                var viewObject = new GameObject("Progress view"); viewObject.transform.SetParent(root.transform, false);
                var view = viewObject.AddComponent<HubProgressView>();
                view.Initialize(root.transform, null);
                view.ApplyViewport(HudLayout.Viewport(960, 600));
                view.Show(HubProgressTab.Leaderboard);
                view.SetVisible(true);

                var entries = Enumerable.Range(0, 20)
                    .Select(i => new AccountLeaderboardEntry("PlayerName" + i.ToString("D5"), 1000 + i,
                        string.Concat(Enumerable.Repeat("Champion of the Obsidian Citadel and the Frozen Northern Frontier ", 4))))
                    .ToArray();
                Invoke(view, "RenderLeaderboard", new AccountLeaderboard("2026-10", entries,
                    new[] { "2026-10" }, new AccountLeaderboardRank(777, 654321)));

                Canvas.ForceUpdateCanvases();
                var content = view.Root.Find("HubProgressCard/ProgressScroll2/Viewport/Content") as RectTransform;
                Assert.That(content, Is.Not.Null);
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                Canvas.ForceUpdateCanvases();

                var first = content.Find("ProgressRow0") as RectTransform;
                var second = content.Find("ProgressRow1") as RectTransform;
                var third = content.Find("ProgressRow2") as RectTransform;
                Assert.That(first, Is.Not.Null); Assert.That(second, Is.Not.Null); Assert.That(third, Is.Not.Null);
                Assert.That(first.rect.height, Is.GreaterThan(0));
                Assert.That(second.rect.height, Is.GreaterThan(first.rect.height), "an entry card grows to fit its separate title and score lines");
                Assert.That(third.rect.height, Is.EqualTo(second.rect.height).Within(.1f));
                Assert.That(first.rect.width, Is.GreaterThan(600), "the narrow-viewport card receives a real layout width");
                var firstCorners = new Vector3[4]; var secondCorners = new Vector3[4];
                first.GetWorldCorners(firstCorners); second.GetWorldCorners(secondCorners);
                Assert.That(firstCorners[0].y - secondCorners[1].y, Is.EqualTo(10).Within(.1f),
                    "adjacent leaderboard cards have a real ten-unit world-space gap");
                var rowText = second.Find("Value").GetComponent<TMPro.TMP_Text>();
                Assert.That(rowText.gameObject.activeInHierarchy, Is.True);
                Assert.That(rowText.text, Does.Contain("PlayerName"));
                Assert.That(rowText.rectTransform.rect.width, Is.GreaterThan(500), "the text child is laid out inside the card");
                Assert.That(rowText.rectTransform.rect.height, Is.GreaterThan(15), "the title text child has visible one-line height");
                Assert.That(rowText.enableWordWrapping, Is.False, "the title cannot grow the card onto more lines");
                var titleSize = rowText.GetPreferredValues(rowText.text);
                Assert.That(titleSize.x, Is.GreaterThan(rowText.rectTransform.rect.width),
                    "the extreme title exceeds its one-line box and exercises ellipsis");
                Assert.That(titleSize.y, Is.LessThanOrEqualTo(rowText.rectTransform.rect.height), "the title line is not clipped vertically");
                Assert.That(rowText.overflowMode, Is.EqualTo(TMPro.TextOverflowModes.Ellipsis));
                var scoreText = second.Find("Detail").GetComponent<TMPro.TMP_Text>();
                Assert.That(scoreText.text, Is.EqualTo("GV  ·  1,000"), "the score is preserved in its independent text widget");
                Assert.That(scoreText.gameObject.activeSelf, Is.True);
                Assert.That(scoreText.rectTransform.rect.width, Is.GreaterThan(500));
                Assert.That(scoreText.rectTransform.rect.height, Is.GreaterThan(15));
                var scoreSize = scoreText.GetPreferredValues(scoreText.text);
                Assert.That(scoreSize.x, Is.LessThanOrEqualTo(scoreText.rectTransform.rect.width));
                Assert.That(scoreSize.y, Is.LessThanOrEqualTo(scoreText.rectTransform.rect.height));

                float fullHeight = content.rect.height;
                Invoke(view, "RenderLeaderboard", new AccountLeaderboard("2026-10", new[] { entries[0] },
                    new[] { "2026-10" }, null));
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                Canvas.ForceUpdateCanvases();
                Assert.That(Enumerable.Range(0, content.childCount).Count(i => content.GetChild(i).gameObject.activeSelf),
                    Is.EqualTo(2), "shrinking the board hides the unused row cards from layout");
                Assert.That(content.rect.height, Is.LessThan(fullHeight), "the scroll content shrinks with its active rows");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void AchievementRowsShowServerFactsAndPreserveUnknownFutureIds()
        {
            var profile = Profile(new Dictionary<string, long> { ["known"] = 1_798_000_000_123L, ["added_by_server"] = 1_798_000_000_456L });
            var catalog = new[]
            {
                new AccountAchievementDefinition("known", "Known", "Recorded by the server.", new[] { "title-known" }),
                new AccountAchievementDefinition("unearned", "Not recorded", "Not yet in the server record.", new string[0])
            };
            IReadOnlyList<string> rows = HubProgressView.BuildAchievementRows(profile, catalog);
            Assert.AreEqual(3, rows.Count);
            StringAssert.Contains("EARNED  ·  Known", rows[0]);
            StringAssert.Contains("RECORDED  ·  2026-12-", rows[0]);
            StringAssert.Contains("NOT RECORDED  ·  Not recorded", rows[1]);
            StringAssert.Contains("UNKNOWN ACHIEVEMENT  ·  added_by_server", rows[2]);
            StringAssert.Contains("2026-12-", rows[2]);
            Assert.IsFalse(rows.Any(x => x.Contains("progress") || x.Contains("unlock")));
            Assert.Throws<System.NotSupportedException>(() => ((IDictionary<string, long>)profile.Achievements).Add("local", 1));
        }

        [Test]
        public async Task OwnerRankIsClearedOnCancelAndSelectionChangeAndLatePrivateBoardCannotRestoreIt()
        {
            var ownerObject = new GameObject("Progress owner privacy fixture");
            var root = new GameObject("Progress view privacy fixture", typeof(RectTransform));
            var transport = new ProgressTransport();
            try
            {
                var owner = ownerObject.AddComponent<AccountCoordinator>();
                owner.Initialize(AccountEndpoint.FromPage("https://local-test.invalid/"), transport, new Credentials());
                Assert.IsTrue(await owner.Flow.OpenProfileAsync("alice"));
                Assert.AreEqual(1, transport.ProfileReads);

                var viewObject = new GameObject("Progress view"); viewObject.transform.SetParent(root.transform, false);
                var view = viewObject.AddComponent<HubProgressView>();
                view.Initialize(root.transform, owner);
                Assert.IsFalse(view.IncludeMyRank, "private rank starts opt-out");
                view.Show(HubProgressTab.Leaderboard);
                view.SetVisible(true);
                Assert.AreEqual(1, transport.PublicBoardReads);
                Assert.AreEqual(1, transport.ProfileReads, "public board requests never claim/open a profile");

                Invoke(view, "ToggleOwnRank");
                Assert.IsTrue(view.IncludeMyRank);
                Assert.AreEqual(1, transport.OwnerBoardReads);
                AssertHasOwnerRank(view);

                Invoke(view, "BeginLeaderboardRead"); // second owner-bound request is held until after cancellation
                Invoke(view, "RenderLeaderboard", Board(new AccountLeaderboardRank(777, 654321))); // seed prior owner row while callback is pending
                AssertHasOwnerRank(view);
                owner.Flow.Cancel(); // same selected name, ownership generation changes
                AssertNoOwnerRank(view);
                Assert.IsTrue(view.IncludeMyRank, "cancelling the same selected owner clears data but retains that owner's explicit opt-in");

                transport.LateOwnerBoard.TrySetResult(new AccountApiResponse(200, leaderboard: Board(new AccountLeaderboardRank(777, 654321))));
                await Task.Yield();
                await Task.Yield();
                AssertNoOwnerRank(view, "late owner-bound response after cancel must not republish private rank");

                owner.Flow.SelectName("bob");
                Assert.IsFalse(view.IncludeMyRank, "changing the selected name resets private-rank opt-in");
                AssertNoOwnerRank(view);
                var toggle = view.Root.Find("HubProgressCard/ToggleOwnRank");
                Assert.That(toggle.GetComponentInChildren<TMPro.TMP_Text>().text, Is.EqualTo("INCLUDE MY RANK · OFF"));
                Assert.AreEqual(1, transport.ProfileReads, "selection changes do not create profile claims");
            }
            finally
            {
                transport.LateOwnerBoard.TrySetResult(new AccountApiResponse(200, leaderboard: Board(new AccountLeaderboardRank(777, 654321))));
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(ownerObject);
            }
        }

        sealed class Credentials : IAccountCredentialStore
        {
            public string Read(string endpointScope, string normalizedName) => null;
            public bool WriteConfirmed(string endpointScope, string normalizedName, string token) => false;
        }

        static AccountFullProfile Profile(string name) => new AccountFullProfile(name, 500, 1000,
            new Dictionary<string, long>(), new AccountFullStats(0, 0, 0, 0, 0),
            new AccountFullCosmetics(new string[0], new Dictionary<string, string>()),
            new Dictionary<string, long>(), new AccountFullProgress(0, 0, 0, new Dictionary<string, long>(), new Dictionary<string, long>()));

        static AccountLeaderboard Board(AccountLeaderboardRank you) => new AccountLeaderboard("2026-10",
            new[] { new AccountLeaderboardEntry("Public", 100, "Champion") }, new[] { "2026-10" }, you);

        static void Invoke(HubProgressView view, string name, params object[] args)
        {
            var method = typeof(HubProgressView).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "missing progress method " + name);
            method.Invoke(view, args);
        }

        static List<string> AllLeaderboardRowText(HubProgressView view)
        {
            var pools = (Array)typeof(HubProgressView).GetField("_rows", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            object pool = pools.GetValue((int)HubProgressTab.Leaderboard);
            var rows = (List<TMPro.TMP_Text>)pool.GetType().GetField("_rows", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pool);
            return rows.Select(row => row.text).ToList();
        }

        static List<string> AllLeaderboardDetailText(HubProgressView view)
        {
            var content = view.Root.Find("HubProgressCard/ProgressScroll2/Viewport/Content");
            return content.GetComponentsInChildren<TMPro.TMP_Text>(true)
                .Where(text => text.name == "Detail").Select(text => text.text).ToList();
        }

        static void AssertHasOwnerRank(HubProgressView view)
        {
            Assert.IsTrue(AllLeaderboardRowText(view).Any(row => row.Contains("YOUR RANK")));
            Assert.IsTrue(AllLeaderboardDetailText(view).Any(detail => detail.Contains("654,321")), "private GV detail is present while opted in");
        }
        static void AssertNoOwnerRank(HubProgressView view, string message = null)
        {
            Assert.IsFalse(AllLeaderboardRowText(view).Any(row => row.Contains("YOUR RANK")), message);
            Assert.IsFalse(AllLeaderboardDetailText(view).Any(detail => detail.Contains("654,321")), "private GV detail was cleared from the pooled row");
        }
    }
}
