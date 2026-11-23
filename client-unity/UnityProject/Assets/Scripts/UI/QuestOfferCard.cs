using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>The quest offer card: one offer the player can accept where they stand. Opened and closed by the HUD;
    /// it sends nothing itself. (Stub of P-UI-QUEST-OFFER-1: every member returns a default.)</summary>
    public sealed class QuestOfferCard
    {
        public const float AcceptSeconds = 2.5f, GraceSeconds = .5f;
        public QuestOfferCard(Transform parent, System.Action onAccept, System.Action onClose) { }
        public RectTransform Root { get { return null; } }
        public bool IsOpen { get { return false; } }
        public QuestHandle? Shown { get { return null; } }
        public float WantedHeight { get { return 0f; } }
        public bool Accepting { get { return false; } }
        public bool Lapsed { get { return false; } }
        public void Open(QuestRowView offer, int inReach, double serverMs) { }
        public void Refresh(QuestRowView offer, int inReach, double serverMs) { }
        public void NoteReach(bool canAccept) { }
        public void Close() { }
        public void Place(HudRegion region) { }
        public void MarkAccepting() { }
        public void Tick(float dt) { }
    }
}
