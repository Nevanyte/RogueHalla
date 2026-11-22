using NUnit.Framework;
using Roguehalla.Client;
using Roguehalla.Net.Ui;

public sealed class LobbyRosterEditModeTests
{
    const string Sample =
        "{\"serverNow\":1731234567890,\"rooms\":[{\"phase\":\"lobby\",\"startedAt\":null,\"humanMin\":3,\"fillTarget\":0,\"humans\":1," +
        "\"players\":[{\"name\":\"Alpha\",\"kind\":\"human\"},{\"name\":\"bot1\",\"kind\":\"bot\"}]}]," +
        "\"waiting\":[{\"name\":\"bravo\"}],\"onlineCount\":2,\"arenaLimit\":16,\"arenaRoomCount\":1,\"arenaAvailable\":true}";

    [Test]
    public void HubPanelShowsGatheringRoomsThenRunningOnesThenTheHub()
    {
        const string busy =
            "{\"serverNow\":1,\"rooms\":[" +
            "{\"phase\":\"active\",\"startedAt\":5,\"humanMin\":2,\"fillTarget\":6,\"humans\":2,\"players\":[{\"name\":\"Cy\",\"kind\":\"human\"},{\"name\":\"Dee\",\"kind\":\"human\"},{\"name\":\"b1\",\"kind\":\"bot\"}]}," +
            "{\"phase\":\"lobby\",\"startedAt\":null,\"humanMin\":3,\"fillTarget\":0,\"humans\":1,\"players\":[{\"name\":\"Alpha\",\"kind\":\"human\"}]}]," +
            "\"waiting\":[{\"name\":\"bravo\"},{\"name\":\"echo\"}],\"onlineCount\":5,\"arenaLimit\":16,\"arenaRoomCount\":2,\"arenaAvailable\":true}";
        string text = HubLobbyActivityView.Compose(LobbyActivity.Parse(busy), 1000, true);
        Assert.AreEqual(
            "Players online · 5\n" +
            "Gathering players · 1/3\nAlpha\n" +
            "Run in progress · 2 players\nCy, Dee · 1 bot\n" +
            "In the hub · 2\nbravo, echo", text);
    }

    [Test]
    public void HubPanelColoursOnlyItsOwnLinesWhenStyled()
    {
        var style = new HubLobbyActivityView.Style("#111111", "#222222", "#333333");
        string text = HubLobbyActivityView.Compose(LobbyActivity.Parse(Sample), 1000, true, style);
        StringAssert.StartsWith("<b>Players online · 2</b>\n<color=#222222>Gathering players · 1/3</color>\nAlpha · 1 bot\n", text);
        StringAssert.EndsWith("<color=#111111>In the hub · 1</color>\nbravo", text);
    }

    [Test]
    public void EmptyServerSaysSoInOneLine()
    {
        string quiet = "{\"serverNow\":1,\"rooms\":[],\"waiting\":[],\"onlineCount\":0,\"arenaLimit\":16,\"arenaRoomCount\":0,\"arenaAvailable\":true}";
        Assert.AreEqual("Players online · 0\n" + LobbyActivity.NobodyOnline, HubLobbyActivityView.Compose(LobbyActivity.Parse(quiet), 0, true));
    }

    [Test]
    public void StaleOrMissingDataShowsTheUnavailableTexts()
    {
        string stale = HubLobbyActivityView.Compose(LobbyActivity.Parse(Sample), 30001, true);
        StringAssert.Contains("Lobby activity is out of date. Refreshing…", stale);
        StringAssert.Contains("Current players unavailable.", stale);
        string never = HubLobbyActivityView.Compose(LobbyActivityModel.Empty, 0, false);
        StringAssert.Contains("Lobby activity is unavailable. Retrying…", never);
    }

    [Test]
    public void PresenceBodyRefusesNamesOutsideTheServerAlphabet()
    {
        Assert.AreEqual("{\"name\":\"Alpha\",\"tabId\":\"0123456789abcdef\"}", LobbyActivityTransport.PresenceBody("Alpha", "0123456789abcdef"));
        Assert.IsNull(LobbyActivityTransport.PresenceBody("Al\"pha", "0123456789abcdef"));
        Assert.IsNull(LobbyActivityTransport.PresenceBody("Alpha", "short"));
    }
}
