using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using ImGuiNET;

namespace GameboySharp
{
    /// <summary>
    /// The ImGui toolbar pinned to the top of the game window. It is a thin "view": it draws buttons
    /// and a status line, and forwards clicks to the emulator or to host-supplied callbacks. It owns
    /// no emulator logic of its own, which keeps the wiring easy to follow.
    ///
    /// Actions the toolbar can't perform by itself (toggling the separate debug window, opening a ROM
    /// via a file dialog, opening the settings modal) are exposed as nullable callbacks that the host
    /// wires up. A button whose callback isn't wired yet is shown disabled rather than hidden, so the
    /// layout stays stable as later phases fill in behaviour.
    /// </summary>
    internal class Toolbar
    {
        /// <summary>Toolbar height in ImGui units. The game viewport is offset down by this much.</summary>
        public const float HeightPoints = 34f;

        private readonly Emulator _emulator;

        // Host-supplied callbacks. Null means "not available yet" → the button renders disabled.
        public Func<bool>? IsDebugWindowVisible;
        public Action? ToggleDebugWindow;
        public Action? OpenRomRequested;
        public Action? ResetRequested;
        public Action? OpenSettingsRequested;

        // Recent-ROMs support: a getter for the list and a callback to load a chosen path.
        public Func<IReadOnlyList<string>>? GetRecentRoms;
        public Action<string>? LoadRomPath;

        // Save-state support: the slot manager plus a provider that turns a slot's thumbnail into an
        // ImGui texture handle (the toolbar can't create GL textures itself).
        public SaveStateManager? StateManager;
        public Func<int, nint>? GetSlotThumbnail;

        // Speed / fast-forward support.
        public Func<double>? GetSpeed;        // current normal-speed multiplier
        public Action? CycleSpeed;            // cycle 0.5x → 1x → 2x
        public Action? FrameAdvanceRequested; // run a single frame while paused
        public Func<bool>? IsTurboActive;     // whether the turbo key is currently held

        public Toolbar(Emulator emulator)
        {
            _emulator = emulator;
        }

        /// <summary>
        /// Draws the toolbar for this frame. Must be called between the ImGui controller's Update and
        /// Render. <paramref name="fps"/> is shown in the status line.
        /// </summary>
        public void Draw(double fps)
        {
            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(viewport.Pos);
            ImGui.SetNextWindowSize(new Vector2(viewport.Size.X, HeightPoints));

            // A fixed, undecorated bar across the top: no title, no resize/move, always at the back so
            // it never steals focus from dialogs drawn on top of it.
            const ImGuiWindowFlags flags =
                ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
                ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings |
                ImGuiWindowFlags.NoBringToFrontOnFocus;

            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);

            if (ImGui.Begin("##toolbar", flags))
            {
                CallbackButton("Open", OpenRomRequested);
                ImGui.SameLine();

                DrawRecentButton();
                ImGui.SameLine();

                DrawPlayPauseButton();
                ImGui.SameLine();

                DrawFrameAdvanceButton();
                ImGui.SameLine();

                CallbackButton("Reset", ResetRequested);
                ImGui.SameLine();

                DrawSpeedButton();
                ImGui.SameLine();

                DrawDebugButton();
                ImGui.SameLine();

                CallbackButton("Settings", OpenSettingsRequested);
                ImGui.SameLine();

                DrawStateButtons();
                ImGui.SameLine();

                DrawStatus(fps);
            }
            ImGui.End();

            ImGui.PopStyleVar(2);
        }

        /// <summary>Toggles emulation between running and paused via the existing CPU pause controls.</summary>
        private void DrawPlayPauseButton()
        {
            bool paused = _emulator.IsPaused;
            if (ImGui.Button(paused ? "Play" : "Pause"))
            {
                if (paused) _emulator.Cpu.Continue();
                else _emulator.Cpu.Pause();
            }
        }

        /// <summary>Single-frame step button, enabled only while paused.</summary>
        private void DrawFrameAdvanceButton()
        {
            bool enabled = _emulator.IsPaused && FrameAdvanceRequested != null;
            if (!enabled) ImGui.BeginDisabled();
            if (ImGui.Button("Frame Adv")) FrameAdvanceRequested?.Invoke();
            if (!enabled) ImGui.EndDisabled();
        }

        /// <summary>A button showing the current speed multiplier; clicking cycles 0.5x → 1x → 2x.</summary>
        private void DrawSpeedButton()
        {
            double speed = GetSpeed?.Invoke() ?? 1.0;
            bool wired = CycleSpeed != null;
            if (!wired) ImGui.BeginDisabled();
            if (ImGui.Button($"Speed {speed:0.##}x")) CycleSpeed?.Invoke();
            if (!wired) ImGui.EndDisabled();
        }

        /// <summary>A "Recent" button that drops down a menu of recently opened ROMs.</summary>
        private void DrawRecentButton()
        {
            var recents = GetRecentRoms?.Invoke();
            bool hasRecents = recents != null && recents.Count > 0 && LoadRomPath != null;

            if (!hasRecents) ImGui.BeginDisabled();
            if (ImGui.Button("Recent")) ImGui.OpenPopup("recent_roms");
            if (!hasRecents) ImGui.EndDisabled();

            if (ImGui.BeginPopup("recent_roms"))
            {
                if (recents != null)
                {
                    foreach (string path in recents)
                    {
                        if (ImGui.MenuItem(Path.GetFileName(path)))
                        {
                            LoadRomPath?.Invoke(path);
                        }
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip(path);
                        }
                    }
                }
                ImGui.EndPopup();
            }
        }

        /// <summary>Toggles the separate debug window; the label reflects its current visibility.</summary>
        private void DrawDebugButton()
        {
            bool visible = IsDebugWindowVisible?.Invoke() ?? false;
            bool wired = ToggleDebugWindow != null;

            if (!wired) ImGui.BeginDisabled();
            if (ImGui.Button(visible ? "Hide Debug" : "Show Debug"))
            {
                ToggleDebugWindow?.Invoke();
            }
            if (!wired) ImGui.EndDisabled();
        }

        /// <summary>A button that invokes <paramref name="action"/>, rendered disabled when unwired.</summary>
        private static void CallbackButton(string label, Action? action)
        {
            bool wired = action != null;
            if (!wired) ImGui.BeginDisabled();
            if (ImGui.Button(label)) action?.Invoke();
            if (!wired) ImGui.EndDisabled();
        }

        /// <summary>Quicksave/quickload buttons for the current slot, plus a "States" slot menu.</summary>
        private void DrawStateButtons()
        {
            bool available = StateManager != null && _emulator.HasRom;
            if (!available) ImGui.BeginDisabled();

            int slot = StateManager?.CurrentSlot ?? 0;
            if (ImGui.Button($"Save [{slot}]")) StateManager?.QuickSave();
            ImGui.SameLine();
            if (ImGui.Button($"Load [{slot}]")) StateManager?.QuickLoad();
            ImGui.SameLine();
            if (ImGui.Button("States")) ImGui.OpenPopup("states_popup");

            if (!available) ImGui.EndDisabled();

            DrawStatesPopup();
        }

        /// <summary>The slot menu: per-slot thumbnail, timestamp, and save/load/select controls.</summary>
        private void DrawStatesPopup()
        {
            if (!ImGui.BeginPopup("states_popup")) return;

            if (StateManager == null)
            {
                ImGui.EndPopup();
                return;
            }

            ImGui.TextDisabled("Number keys 0-9 select the quick slot.");
            ImGui.Separator();

            for (int i = 0; i < SaveStateManager.SlotCount; i++)
            {
                ImGui.PushID(i);

                bool isCurrent = i == StateManager.CurrentSlot;
                if (ImGui.RadioButton($"Slot {i}", isCurrent))
                {
                    StateManager.CurrentSlot = i;
                }

                ImGui.SameLine();
                System.DateTime? timestamp = StateManager.Timestamp(i);
                ImGui.TextDisabled(timestamp.HasValue ? timestamp.Value.ToString("yyyy-MM-dd HH:mm:ss") : "(empty)");

                nint thumbnail = GetSlotThumbnail?.Invoke(i) ?? 0;
                if (thumbnail != 0)
                {
                    ImGui.Image(thumbnail, new Vector2(80, 72));
                    ImGui.SameLine();
                }

                if (ImGui.SmallButton("Save")) StateManager.SaveSlot(i);
                ImGui.SameLine();

                bool hasState = StateManager.HasState(i);
                if (!hasState) ImGui.BeginDisabled();
                if (ImGui.SmallButton("Load")) StateManager.LoadSlot(i);
                if (!hasState) ImGui.EndDisabled();

                ImGui.Separator();
                ImGui.PopID();
            }

            ImGui.EndPopup();
        }

        private void DrawStatus(double fps)
        {
            string state = _emulator.IsPaused ? "PAUSED" : "running";
            int slot = StateManager?.CurrentSlot ?? 0;
            string turbo = (IsTurboActive?.Invoke() ?? false) ? "  >> TURBO" : "";
            ImGui.Text($"|  {_emulator.CurrentRomName}   {fps:0} FPS   {state}   slot {slot}{turbo}");
        }
    }
}
