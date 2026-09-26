#nullable enable
using System;

namespace VividWorld.Core.Presentation
{
    public enum EncyclopediaReturnState
    {
        Idle,
        WaitingForOpen,
        WaitingForClose
    }

    public enum EncyclopediaReturnAction
    {
        None,
        Reopen,
        GaveUp
    }

    public sealed class EncyclopediaReturnTracker
    {
        public const int MaxOpenWaitFrames = 30;

        private EncyclopediaReturnState _state = EncyclopediaReturnState.Idle;
        private string? _currentLink;
        private int _openWaitFrames;
        private int _openedAfterFrames;
        private string? _lastReason;
        private bool _justOpened;

        public EncyclopediaReturnState State => _state;
        public string? CurrentLink => _currentLink;
        public int OpenWaitFrames => _openWaitFrames;
        public int OpenedAfterFrames => _openedAfterFrames;
        public string? LastReason => _lastReason;
        public bool JustOpened => _justOpened;

        public void Arm(string link)
        {
            _state = EncyclopediaReturnState.WaitingForOpen;
            _currentLink = link;
            _openWaitFrames = 0;
            _openedAfterFrames = 0;
            _lastReason = null;
            _justOpened = false;
        }

        public void Cancel(string reason)
        {
            _state = EncyclopediaReturnState.Idle;
            _lastReason = reason;
            _justOpened = false;
        }

        public EncyclopediaReturnAction Step(bool? encyclopediaOpen)
        {
            return Step(encyclopediaOpen, out _);
        }

        public EncyclopediaReturnAction Step(bool? encyclopediaOpen, out string? reason)
        {
            _justOpened = false;
            reason = null;

            if (_state == EncyclopediaReturnState.Idle)
            {
                return EncyclopediaReturnAction.None;
            }

            if (encyclopediaOpen == null)
            {
                _state = EncyclopediaReturnState.Idle;
                _lastReason = "map screen or encyclopedia view not available";
                reason = _lastReason;
                return EncyclopediaReturnAction.GaveUp;
            }

            if (_state == EncyclopediaReturnState.WaitingForOpen)
            {
                if (encyclopediaOpen.Value)
                {
                    _openWaitFrames++;
                    _openedAfterFrames = _openWaitFrames;
                    _justOpened = true;
                    _state = EncyclopediaReturnState.WaitingForClose;
                    return EncyclopediaReturnAction.None;
                }
                else
                {
                    _openWaitFrames++;
                    if (_openWaitFrames >= MaxOpenWaitFrames)
                    {
                        _state = EncyclopediaReturnState.Idle;
                        _lastReason = "encyclopedia never opened";
                        reason = _lastReason;
                        return EncyclopediaReturnAction.GaveUp;
                    }
                    return EncyclopediaReturnAction.None;
                }
            }

            if (_state == EncyclopediaReturnState.WaitingForClose)
            {
                if (!encyclopediaOpen.Value)
                {
                    _state = EncyclopediaReturnState.Idle;
                    return EncyclopediaReturnAction.Reopen;
                }
                else
                {
                    return EncyclopediaReturnAction.None;
                }
            }

            return EncyclopediaReturnAction.None;
        }
    }
}
