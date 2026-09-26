#nullable enable

using System;
using System.Collections.Generic;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;
using TaleWorlds.ScreenSystem;
using VividWorld.Core.Config;
using VividWorld.Core.Presentation;

namespace VividWorld.UI
{
    internal static class ChronicleWindowManager
    {
        private static GauntletLayer? _layer;
        private static GauntletMovieIdentifier? _movie;
        private static ScreenBase? _host;
        private static ChronicleWindowVM? _vm;
        private static PresentationConfig? _presentationConfig;
        private static bool _pendingLayoutLog;
        private static int _openFrameCount;
        private static string? _pendingLink;

        internal static readonly EncyclopediaReturnTracker ReturnTracker = new EncyclopediaReturnTracker();

        internal static bool IsOpen => _layer != null;

        internal static void Open(ChronicleWindowVM vm, PresentationConfig? presentationConfig = null)
        {
            if (IsOpen) return;
            try
            {
                _vm = vm;
                _presentationConfig = presentationConfig;
                _pendingLayoutLog = true;
                _openFrameCount = 0;
                _layer = new GauntletLayer("VividWorldChronicle", 4500);
                _movie = _layer.LoadMovie("VividWorldChronicle", vm);
                _layer.InputRestrictions.SetInputRestrictions();
                _layer.IsFocusLayer = true;
                _host = ScreenManager.TopScreen;
                _host?.AddLayer(_layer);
                ScreenManager.TrySetFocus(_layer);
            }
            catch (Exception ex)
            {
                ModLog.Error("Chronicle window failed to open", ex);
                Close();
            }
        }

        /// <summary>
        /// 點連結的事件是在這一層自己的 <c>LateUpdate</c> 途中發的，而 <c>RemoveLayer</c> 會當場把層釋放
        /// （帳本 D-81）。所以這裡只記下來，關窗與開百科留到 <see cref="Tick"/>——那是從
        /// <c>OnApplicationTick</c> 叫的，跟 Esc 關窗同一條路。
        /// </summary>
        internal static void OpenLink(string link)
        {
            bool linksEnabled = _presentationConfig?.EncyclopediaLinksEnabled ?? true;
            if (!linksEnabled)
            {
                ModLog.Info(ChronicleLogFormatter.FormatIgnoredLink(link));
                return;
            }

            if (_pendingLink != null) return;
            _pendingLink = link;
            ModLog.Info(ChronicleLogFormatter.FormatLinkQueued(link));
            ModLog.Flush();
        }

        private static void ProcessPendingLink()
        {
            string link = _pendingLink!;
            _pendingLink = null;
            try
            {
                ModLog.Info(ChronicleLogFormatter.FormatOpeningLink(link));
                Close();
                ReturnTracker.Arm(link);
                var encMgr = TaleWorlds.CampaignSystem.Campaign.Current?.EncyclopediaManager;
                if (encMgr == null)
                {
                    throw new InvalidOperationException("Campaign.Current or EncyclopediaManager is not available");
                }
                encMgr.GoToLink(link);
            }
            catch (Exception ex)
            {
                ModLog.Error($"Failed to open encyclopedia link '{link}'", ex);
                string reason = "error: " + ex.Message;
                ReturnTracker.Cancel(reason);
                ModLog.Info(ChronicleLogFormatter.FormatNotReopening(reason));
            }
            ModLog.Flush();
        }

        internal static void Close()
        {
            if (_layer == null) return;
            try
            {
                _layer.IsFocusLayer = false;
                _layer.InputRestrictions.ResetInputRestrictions();
                _host?.RemoveLayer(_layer);
            }
            catch (Exception ex)
            {
                ModLog.Error("Chronicle window failed to close cleanly", ex);
            }
            finally
            {
                _layer = null;
                _movie = null;
                _host = null;
                _vm = null;
                _pendingLayoutLog = false;
                _openFrameCount = 0;
                _pendingLink = null;
            }
        }

        internal static void Tick()
        {
            if (!IsOpen) return;
            if (_pendingLink != null)
            {
                ProcessPendingLink();
                return;
            }

            try
            {
                var input = _layer?.Input;
                if (input != null && input.IsKeyReleased(InputKey.Escape))
                {
                    Close();
                    return;
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("Chronicle window tick failed", ex);
            }

            if (_pendingLayoutLog)
            {
                try
                {
                    CheckAndLogLayout();
                }
                catch (Exception ex)
                {
                    ModLog.Error("Chronicle layout logging failed", ex);
                    _pendingLayoutLog = false;
                }
            }
        }

        private static void CheckAndLogLayout()
        {
            _openFrameCount++;

            var root = _layer?.UIContext?.Root;
            var listWidget = root?.FindChild("ChronicleList", true);

            bool listFound = listWidget != null;
            int childCount = listWidget?.ChildCount ?? 0;
            int entryCount = _vm?.Entries?.Count ?? 0;

            bool allRowsHaveHeight = false;
            if (listFound && listWidget != null)
            {
                if (entryCount == 0 && childCount == 0)
                {
                    allRowsHaveHeight = true;
                }
                else if (childCount > 0 && childCount >= entryCount)
                {
                    allRowsHaveHeight = true;
                    for (int i = 0; i < childCount; i++)
                    {
                        var child = listWidget.GetChild(i);
                        if (child == null || child.Size.Y <= 0.5f)
                        {
                            allRowsHaveHeight = false;
                            break;
                        }
                    }
                }
            }

            if (!allRowsHaveHeight && _openFrameCount < 10)
            {
                return;
            }

            var clipWidget = root?.FindChild("ChronicleClip", true) ?? root?.FindChild("ChronicleScroller", true);
            double viewportHeight = clipWidget != null ? clipWidget.Size.Y : 0.0;

            List<ChronicleRowLayout>? rowLayouts = null;
            if (listFound && listWidget != null)
            {
                rowLayouts = new List<ChronicleRowLayout>(childCount);
                float baseY = childCount > 0 ? listWidget.GetChild(0).GlobalPosition.Y : 0f;
                for (int i = 0; i < childCount; i++)
                {
                    var child = listWidget.GetChild(i);
                    if (child == null) continue;
                    double relY = child.GlobalPosition.Y - baseY;
                    double h = child.Size.Y;
                    bool vis = child.IsVisible;
                    rowLayouts.Add(new ChronicleRowLayout(relY, h, vis));
                }
            }

            string logText = ChronicleLogFormatter.FormatLayout(
                _openFrameCount,
                entryCount,
                viewportHeight,
                rowLayouts,
                listFound,
                out string verdict);

            if (verdict == "ok")
            {
                ModLog.Info(logText);
            }
            else
            {
                ModLog.Warn(logText);
            }

            _pendingLayoutLog = false;
        }
    }
}
