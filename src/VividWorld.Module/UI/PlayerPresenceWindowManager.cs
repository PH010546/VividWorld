#nullable enable

using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;
using TaleWorlds.TwoDimension;
using VividWorld.Core.Config;

namespace VividWorld.UI
{
    /// <summary>
    /// 開關玩家入局視窗。開著的時候大地圖時間停住、遊戲暫停，關窗時全部還原。
    /// 開關的做法照原版隨機事件視窗，但不走它的事件流程。
    /// </summary>
    internal static class PlayerPresenceWindowManager
    {
        private static GauntletLayer? _layer;
        private static GauntletMovieIdentifier? _movie;
        private static ScreenBase? _host;
        private static PlayerPresenceWindowVM? _vm;
        private static SpriteCategory? _spriteCategory;
        private static bool _stateSaved;
        private static CampaignTimeControlMode _savedMode;
        private static bool _savedLock;
        private static bool _paused;

        internal static bool IsOpen => _layer != null;

        /// <summary>開窗。開不起來（例外）時把已經做的還原、寫錯誤日誌並回傳 false。</summary>
        internal static bool Open(PlayerPresenceWindowVM vm)
        {
            if (IsOpen) return false;
            try
            {
                _vm = vm;
                _spriteCategory = UIResourceManager.LoadSpriteCategory("ui_map_incidents");

                var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
                if (campaign != null)
                {
                    _savedMode = campaign.TimeControlMode;
                    _savedLock = campaign.TimeControlModeLock;
                    _stateSaved = true;
                    campaign.TimeControlMode = CampaignTimeControlMode.Stop;
                    campaign.SetTimeControlModeLock(true);
                }

                MBCommon.PauseGameEngine();
                _paused = true;

                _layer = new GauntletLayer("VividWorldPresence", 4500);
                _movie = _layer.LoadMovie("VividWorldPresence", vm);
                _layer.InputRestrictions.SetInputRestrictions();
                _layer.IsFocusLayer = true;
                _host = ScreenManager.TopScreen;
                _host?.AddLayer(_layer);
                ScreenManager.TrySetFocus(_layer);
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error("Player presence window failed to open", ex);
                Close();
                return false;
            }
        }

        /// <summary>關窗並還原。順序照原版事件視窗關閉時的做法。沒開著就什麼都不做。</summary>
        internal static void Close()
        {
            if (_layer == null && !_stateSaved && !_paused && _spriteCategory == null) return;

            var layer = _layer;
            try
            {
                if (_paused && MBCommon.IsPaused) MBCommon.UnPauseGameEngine();
            }
            catch (Exception ex)
            {
                ModLog.Error("Player presence window: resuming the game failed", ex);
            }

            try
            {
                if (layer != null)
                {
                    layer.IsFocusLayer = false;
                    layer.InputRestrictions.ResetInputRestrictions();
                    ScreenManager.TryLoseFocus(layer);
                    _host?.RemoveLayer(layer);
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("Player presence window failed to close cleanly", ex);
            }

            try
            {
                var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
                if (_stateSaved && campaign != null)
                {
                    campaign.TimeControlMode = _savedMode;
                    campaign.SetTimeControlModeLock(_savedLock);
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("Player presence window: restoring the time control failed", ex);
            }

            try
            {
                _spriteCategory?.Unload();
            }
            catch (Exception ex)
            {
                ModLog.Error("Player presence window: unloading the sprite category failed", ex);
            }

            _layer = null;
            _movie = null;
            _host = null;
            _vm = null;
            _spriteCategory = null;
            _stateSaved = false;
            _paused = false;
        }
    }
}
