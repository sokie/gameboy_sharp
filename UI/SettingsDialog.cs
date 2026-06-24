using System;
using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;

namespace GameboySharp
{
    /// <summary>
    /// The modal Settings window, organised into Controls / Audio / Video / General tabs. It edits the
    /// <see cref="EmulatorConfig"/> in place, pushes changes to the live emulator each frame via
    /// <see cref="RuntimeConfig"/>, and persists the config when it closes. Key/gamepad rebinding works
    /// by "click to capture": click a binding, then press the key or gamepad button you want.
    /// </summary>
    internal class SettingsDialog
    {
        // The order buttons are listed in the Controls tab (d-pad first, then face/system buttons).
        private static readonly GbButton[] ButtonOrder =
        {
            GbButton.Up, GbButton.Down, GbButton.Left, GbButton.Right,
            GbButton.A, GbButton.B, GbButton.Start, GbButton.Select
        };

        private static readonly Key[] AllKeys = (Key[])Enum.GetValues(typeof(Key));

        private readonly EmulatorConfig _config;
        private readonly Emulator _emulator;
        private readonly ScreenRenderer _renderer;
        private readonly IInputContext _input;
        private readonly Action _persist;

        private bool _isOpen;
        private bool _shouldOpenPopup;

        // "Click to capture" rebinding state.
        private bool _capturing;
        private bool _captureGamepad;
        private GbButton _captureButton;
        private bool _captureArmed; // skips the frame the capture button was clicked

        public SettingsDialog(EmulatorConfig config, Emulator emulator, ScreenRenderer renderer,
                              IInputContext input, Action persist)
        {
            _config = config;
            _emulator = emulator;
            _renderer = renderer;
            _input = input;
            _persist = persist;
        }

        public void Open()
        {
            _isOpen = true;
            _shouldOpenPopup = true;
        }

        public void Draw()
        {
            if (!_isOpen) return;

            if (_shouldOpenPopup)
            {
                ImGui.OpenPopup("Settings");
                _shouldOpenPopup = false;
            }

            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(viewport.Pos + viewport.Size * 0.5f, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
            ImGui.SetNextWindowSize(new Vector2(520, 420), ImGuiCond.Appearing);

            bool stayOpen = true;
            if (ImGui.BeginPopupModal("Settings", ref stayOpen))
            {
                if (ImGui.BeginTabBar("settings_tabs"))
                {
                    if (ImGui.BeginTabItem("Controls")) { DrawControlsTab(); ImGui.EndTabItem(); }
                    if (ImGui.BeginTabItem("Audio")) { DrawAudioTab(); ImGui.EndTabItem(); }
                    if (ImGui.BeginTabItem("Video")) { DrawVideoTab(); ImGui.EndTabItem(); }
                    if (ImGui.BeginTabItem("General")) { DrawGeneralTab(); ImGui.EndTabItem(); }
                    ImGui.EndTabBar();
                }

                ImGui.Separator();
                if (ImGui.Button("Close", new Vector2(120, 0)))
                {
                    CloseAndPersist();
                }

                // Push the (possibly edited) settings to the live emulator so changes are heard/seen
                // immediately while the dialog is open.
                RuntimeConfig.ApplyAudio(_config, _emulator.Apu);
                RuntimeConfig.ApplyVideo(_config, _emulator.Ppu, _renderer);

                ImGui.EndPopup();
            }

            if (!stayOpen)
            {
                CloseAndPersist();
            }

            HandleCapture();
        }

        private void DrawControlsTab()
        {
            ImGui.TextWrapped("Click a binding, then press the key or gamepad button to assign. Press Escape to cancel.");
            ImGui.Spacing();

            if (ImGui.BeginTable("controls", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Button");
                ImGui.TableSetupColumn("Keyboard");
                ImGui.TableSetupColumn("Gamepad");
                ImGui.TableHeadersRow();

                foreach (GbButton button in ButtonOrder)
                {
                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(button.ToString());

                    ImGui.TableSetColumnIndex(1);
                    DrawKeyboardBindButton(button);

                    ImGui.TableSetColumnIndex(2);
                    DrawGamepadBindButton(button);
                }

                ImGui.EndTable();
            }
        }

        private void DrawKeyboardBindButton(GbButton button)
        {
            bool capturingThis = _capturing && !_captureGamepad && _captureButton == button;
            string label = capturingThis
                ? "press a key..."
                : (_config.Controls.Keyboard.TryGetValue(button, out Key key) ? key.ToString() : "(unset)");

            if (ImGui.Button($"{label}##kbd_{button}", new Vector2(-1, 0)) && !_capturing)
            {
                BeginCapture(button, gamepad: false);
            }
        }

        private void DrawGamepadBindButton(GbButton button)
        {
            bool capturingThis = _capturing && _captureGamepad && _captureButton == button;
            string label = capturingThis
                ? "press a button..."
                : (_config.Controls.Gamepad.TryGetValue(button, out ButtonName name) ? name.ToString() : "(unset)");

            if (ImGui.Button($"{label}##pad_{button}", new Vector2(-1, 0)) && !_capturing)
            {
                BeginCapture(button, gamepad: true);
            }
        }

        private void DrawAudioTab()
        {
            float volume = _config.Audio.MasterVolume;
            if (ImGui.SliderFloat("Master volume", ref volume, 0f, 1f)) _config.Audio.MasterVolume = volume;

            bool muted = _config.Audio.Muted;
            if (ImGui.Checkbox("Mute", ref muted)) _config.Audio.Muted = muted;

            ImGui.SeparatorText("Channels");
            bool m1 = _config.Audio.MuteChannel1;
            if (ImGui.Checkbox("Mute Pulse A", ref m1)) _config.Audio.MuteChannel1 = m1;
            bool m2 = _config.Audio.MuteChannel2;
            if (ImGui.Checkbox("Mute Pulse B", ref m2)) _config.Audio.MuteChannel2 = m2;
            bool m3 = _config.Audio.MuteChannel3;
            if (ImGui.Checkbox("Mute Wave", ref m3)) _config.Audio.MuteChannel3 = m3;
            bool m4 = _config.Audio.MuteChannel4;
            if (ImGui.Checkbox("Mute Noise", ref m4)) _config.Audio.MuteChannel4 = m4;

            ImGui.SeparatorText("Backend");
            string[] backends = { "Auto", "SDL", "OpenAL" };
            int backend = (int)_config.Audio.Backend;
            if (ImGui.Combo("Audio backend", ref backend, backends, backends.Length))
            {
                _config.Audio.Backend = (AudioBackend)backend;
            }
            ImGui.TextDisabled("(applies on next launch)");
        }

        private void DrawVideoTab()
        {
            string[] palettes = { "Green", "Grey", "Pocket", "Yellow" };
            int palette = (int)_config.Video.Palette;
            if (ImGui.Combo("DMG palette", ref palette, palettes, palettes.Length))
            {
                _config.Video.Palette = (DmgPalettePreset)palette;
            }

            bool integerScale = _config.Video.IntegerScale;
            if (ImGui.Checkbox("Integer scaling (crisp pixels)", ref integerScale)) _config.Video.IntegerScale = integerScale;

            bool lockAspect = _config.Video.LockAspectRatio;
            if (ImGui.Checkbox("Lock aspect ratio", ref lockAspect)) _config.Video.LockAspectRatio = lockAspect;

            bool scanlines = _config.Video.ScanlineShader;
            if (ImGui.Checkbox("Scanline (LCD) effect", ref scanlines)) _config.Video.ScanlineShader = scanlines;
        }

        private void DrawGeneralTab()
        {
            bool pauseOnFocusLoss = _config.General.PauseOnFocusLoss;
            if (ImGui.Checkbox("Pause when window loses focus", ref pauseOnFocusLoss))
            {
                _config.General.PauseOnFocusLoss = pauseOnFocusLoss;
            }

            bool muteDuringTurbo = _config.General.MuteDuringTurbo;
            if (ImGui.Checkbox("Mute audio during fast-forward", ref muteDuringTurbo))
            {
                _config.General.MuteDuringTurbo = muteDuringTurbo;
            }

            string saveDir = _config.General.SaveDirectory;
            if (ImGui.InputText("Save folder", ref saveDir, 512)) _config.General.SaveDirectory = saveDir;
            ImGui.TextDisabled("Leave empty to save next to each ROM.");
        }

        private void BeginCapture(GbButton button, bool gamepad)
        {
            _capturing = true;
            _captureGamepad = gamepad;
            _captureButton = button;
            _captureArmed = false;
        }

        /// <summary>While a capture is pending, watch for the first key/button press and bind it.</summary>
        private void HandleCapture()
        {
            if (!_capturing) return;

            // Ignore the frame the binding button was clicked, so a keyboard "activate" (Space/Enter)
            // doesn't immediately bind itself.
            if (!_captureArmed)
            {
                _captureArmed = true;
                return;
            }

            IKeyboard? keyboard = _input.Keyboards.Count > 0 ? _input.Keyboards[0] : null;

            if (keyboard != null && keyboard.IsKeyPressed(Key.Escape))
            {
                _capturing = false;
                return;
            }

            if (!_captureGamepad)
            {
                if (keyboard == null) return;
                foreach (Key key in AllKeys)
                {
                    if (key == Key.Unknown || key == Key.Escape) continue;
                    if (keyboard.IsKeyPressed(key))
                    {
                        _config.Controls.Keyboard[_captureButton] = key;
                        _capturing = false;
                        _persist();
                        return;
                    }
                }
            }
            else
            {
                IGamepad? pad = _input.Gamepads.Count > 0 ? _input.Gamepads[0] : null;
                if (pad == null) return;
                foreach (Button b in pad.Buttons)
                {
                    if (b.Pressed)
                    {
                        _config.Controls.Gamepad[_captureButton] = b.Name;
                        _capturing = false;
                        _persist();
                        return;
                    }
                }
            }
        }

        private void CloseAndPersist()
        {
            _capturing = false;
            _isOpen = false;
            _persist();
            ImGui.CloseCurrentPopup();
        }
    }
}
