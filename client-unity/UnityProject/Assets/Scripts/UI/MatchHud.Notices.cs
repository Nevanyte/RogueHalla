using System;
using System.Text;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Roguehalla.Client
{
    /// <summary>HUD part for the server notice feed: its creation, per-frame visibility and the event subscription rotation.</summary>
    public sealed partial class MatchHud
    {
        Action<ServerMsg.Events> _boundNoticeEvents;
        OrderedNoticeFeed _orderedNotices;

        void RotateNoticeEvents()
        {
            var session = _session;
            if (session == null) return;
            if (_boundNoticeEvents != null) session.EventsReceived -= _boundNoticeEvents;
            uint epoch = session.TimelineEpoch;
            Action<ServerMsg.Events> handler = null;
            handler = batch =>
            {
                // An earlier subscriber may reset this same Session or rebind the HUD while
                // the old invocation list is still running. Only this binding may append.
                if (ReferenceEquals(_session, session) && session.TimelineEpoch == epoch &&
                    ReferenceEquals(_boundNoticeEvents, handler)) Events(batch);
            };
            _boundNoticeEvents = handler;
            session.EventsReceived += handler;
        }

        void CreateNoticeFeed()
        {
            _orderedNotices = gameObject.AddComponent<OrderedNoticeFeed>();
            _orderedNotices.Initialize(_root, ClosePlayerFlows);
        }

        void TickNotices()
        {
            _orderedNotices?.SetRecentVisible(_session != null && WorldHudVisible);
        }
    }
}
