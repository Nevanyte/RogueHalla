using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Registers the painted socket of an exposed garnet pane with the landmark accent pool.</summary>
    public static class ChurchGarnetLight
    {
        public static void Sync(Landmark landmark, SpriteRenderer pane, ChurchWindowPaneMode mode,
            Vector2 contactTile, Vector3 socketWorld, Color color, float radius, float intensity, float spillIntensity = .58f)
        {
            if (landmark == null || pane == null) return;
            if (mode != ChurchWindowPaneMode.Full || !pane.enabled || pane.sprite == null
                || !pane.gameObject.activeInHierarchy || pane.color.a <= 0f || radius <= .01f || intensity <= 0f)
            {
                Detach(landmark, pane);
                return;
            }

            var socket = landmark.Root.transform.InverseTransformPoint(socketWorld);
            float lift = Mathf.Max(.05f, socket.y + contactTile.y * ArenaView.GroundSquash);
            var tile = new Vector2(socket.x, contactTile.y);
            landmark.RegisterCandleSocket(pane.GetInstanceID(), tile, lift, color, intensity, radius, spillIntensity, steadyGlass: true);
        }

        public static void Detach(Landmark landmark, SpriteRenderer pane)
        {
            if (landmark != null && pane != null) landmark.RemoveCandleSocket(pane.GetInstanceID());
        }
    }
}
