#nullable enable

using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;
using TaleWorlds.ScreenSystem;

namespace VividWorld.UI
{
    internal static class ChronicleWindowManager
    {
        private static GauntletLayer? _layer;
        private static GauntletMovieIdentifier? _movie;
        private static ScreenBase? _host;
        private static ChronicleWindowVM? _vm;

        internal static bool IsOpen => _layer != null;

        internal static void Open(ChronicleWindowVM vm)
        {
            if (IsOpen) return;
            try
            {
                _vm = vm;
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
            }
        }

        internal static void Tick()
        {
            if (!IsOpen) return;
            try
            {
                var input = _layer?.Input;
                if (input != null && input.IsKeyReleased(InputKey.Escape))
                {
                    Close();
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("Chronicle window tick failed", ex);
            }
        }
    }
}
