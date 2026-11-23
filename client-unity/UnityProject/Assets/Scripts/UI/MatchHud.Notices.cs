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

        /// <summary>Test seam: seconds the toast lanes advance per tick instead of the unscaled frame time. Null in play.</summary>
        public static float? NoticeDtOverride;

        ToastLaneModel _eventModel, _feedbackModel;
        ToastLane _eventLane, _feedbackLane;
        string _stickyNotice = "";
        float _topCentreBottom;
        Action<WireEvent> _noticeAccepted;
        Action _noticesCleared;

        /// <summary>Sets (or, for null or empty, clears) the sticky feedback toast line.</summary>
        public void SetStickyNotice(string text)
        {
            text = text ?? "";
            if (_feedbackModel == null || text == _stickyNotice) return;
            _stickyNotice = text;
            _feedbackModel.SetSticky(text, NoticeTone.Info);
        }

        /// <summary>Builds the two toast lanes (event: top centre under the timer; feedback: bottom centre above the ability row).</summary>
        void CreateToastLanes()
        {
            _eventModel = new ToastLaneModel(HudRegions.EventMax);
            _feedbackModel = new ToastLaneModel(HudRegions.FeedbackMax);
            _eventLane = new ToastLane(_stage, "EventLane", true, HudRegions.EventMax);
            _feedbackLane = new ToastLane(_stage, "FeedbackLane", false, HudRegions.FeedbackMax);
            _stickyNotice = "";
        }

        void RouteNotice(WireEvent item)
        {
            var route = NoticeRouter.Route(item);
            if (route.Lane == NoticeLane.None) return;
            string text = route.Text;
            if (text == null && item.Kind == EventKind.PotionNotice) text = NoticeRouter.Short(PotionNoticeFormatter.Format((PotionNotice)item.PotionNotice));
            (route.Lane == NoticeLane.Event ? _eventModel : _feedbackModel)?.Push(text, route.Tone, route.Key);
        }

        void PushFeedbackLine(string text, NoticeTone tone, string key)
        {
            _feedbackModel?.Push(text, tone, key, CastReadiness.HoldSeconds, dropIfLate: true);
        }

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
            _noticeAccepted = RouteNotice;
            _noticesCleared = () => { _eventModel?.Clear(); _feedbackModel?.Clear(); _stickyNotice = ""; };
            _orderedNotices.Accepted += _noticeAccepted;
            _orderedNotices.Cleared += _noticesCleared;
        }

        void TickNotices()
        {
            if (_eventModel == null) return;
            float dt = NoticeDtOverride ?? Time.unscaledDeltaTime;
            bool visible = WorldHudVisible && _hud.gameObject.activeInHierarchy, reduced = UiMotion.Reduced;
            _eventModel.Tick(dt, visible, reduced);
            _feedbackModel.Tick(dt, visible, reduced);
            float stageWidth = _stage.rect.width;
            float minLeft = stageWidth > 0f ? PlateLeft + HudLayout.PlateWidth(stageWidth) + 8f : 0f;
            _eventLane.SetVisible(visible); _feedbackLane.SetVisible(visible);
            _eventLane.Present(_eventModel, stageWidth, _topCentreBottom, minLeft);
            _feedbackLane.Present(_feedbackModel, stageWidth, 0f, 0f);
        }
    }
}
