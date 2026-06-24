// GameWindow.cs
using System;
using System.IO;
using System.Numerics;
using GameboySharp;
using ImGuiNET;
using Serilog;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;


namespace GameboySharp
{
    internal class GameWindow : IDisposable
    {
        private readonly IWindow _window;
        private readonly Emulator _emulator;
        private readonly EmulatorConfig _config;
        private ScreenRenderer _renderer;
        private GL _gl;
        private IInputContext _inputContext;
        private ImGuiController _imGuiController;
        private readonly Toolbar _toolbar;
        private readonly FileBrowser _fileBrowser;
        private readonly InputManager _inputManager;
        private readonly SettingsDialog _settingsDialog;
        private readonly SaveStateManager _saveStateManager;
        private readonly ThumbnailCache _thumbnailCache;

        // The number-row keys 0-9 select the active quicksave slot (not rebindable).
        private static readonly Key[] SlotKeys =
        {
            Key.Number0, Key.Number1, Key.Number2, Key.Number3, Key.Number4,
            Key.Number5, Key.Number6, Key.Number7, Key.Number8, Key.Number9
        };

        // Set when the user clicks Reset; consumed next frame to open the confirmation modal (ImGui's
        // OpenPopup must be called from within the same window draw, not from a button callback).
        private bool _openResetConfirm;

        // Smoothed frames-per-second, shown in the toolbar and window title.
        private double _fps;
        private int _titleUpdateCounter;

        // True while the turbo/fast-forward key is held. The main loop reads this to drop frame pacing.
        private bool _turboActive;
        public bool TurboActive => _turboActive;

        // Tracks whether the emulator was auto-paused by focus loss, so we only auto-resume our own
        // pause and never override a pause the user set deliberately.
        private bool _pausedByFocusLoss;

        // We expose the underlying IWindow so the main loop can control it.
        public IWindow SilkWindow => _window;

        // The toolbar exposes callbacks (e.g. "toggle the debug window") that only the host knows how
        // to fulfil, so we surface it for Program.cs to wire up.
        public Toolbar Toolbar => _toolbar;

        public GameWindow(Emulator emulator, EmulatorConfig config)
        {
            _emulator = emulator;
            _config = config;

            var options = WindowOptions.Default;
            options.API = new GraphicsAPI(ContextAPI.OpenGL, new APIVersion(3, 3));
            options.Title = "Game Boy Emulator";
            // Restore the last window size, clamped so a bad config value can't make it unusably small.
            int initialWidth = Math.Max(GameboyConstants.ScreenWidth, _config.WindowWidth);
            int initialHeight = Math.Max(GameboyConstants.ScreenHeight, _config.WindowHeight);
            options.Size = new Vector2D<int>(initialWidth, initialHeight);
            options.VSync = false; // We handle frame pacing manually in the main loop

            _window = Window.Create(options);
            _window.Initialize();
            _gl = _window.CreateOpenGL();

            // One shared input context feeds both the emulator (keyboard/gamepad → joypad) and ImGui
            // (mouse/keyboard for the toolbar and dialogs).
            _inputContext = _window.CreateInput();

            // Create our dedicated renderer
            _renderer = new ScreenRenderer(_gl);
            _renderer.Load();

            // Add ImGui on top of the raw GL screen so we can draw a toolbar and (later) dialogs.
            _imGuiController = new ImGuiController(_gl, _window, _inputContext);

            _toolbar = new Toolbar(_emulator);

            // Start the file browser in the folder of the most recent ROM, if we have one.
            string? lastRomDir = _config.RecentRoms.Count > 0 ? Path.GetDirectoryName(_config.RecentRoms[0]) : null;
            _fileBrowser = new FileBrowser(lastRomDir);
            _fileBrowser.OnRomChosen = LoadRomFromUi;

            // Input (keyboard + gamepad → joypad) and the settings dialog both read the shared config.
            _inputManager = new InputManager(_config);
            _settingsDialog = new SettingsDialog(_config, _emulator, _renderer, _inputContext,
                                                 () => ConfigStore.Save(_config));

            // Wire the toolbar's host callbacks to this window's behaviour.
            _toolbar.OpenRomRequested = () => _fileBrowser.Open();
            _toolbar.GetRecentRoms = () => _config.RecentRoms;
            _toolbar.LoadRomPath = LoadRomFromUi;
            _toolbar.ResetRequested = () => _openResetConfirm = true;
            _toolbar.OpenSettingsRequested = () => _settingsDialog.Open();
            _toolbar.GetSpeed = () => _config.General.SpeedMultiplier;
            _toolbar.CycleSpeed = CycleSpeed;
            _toolbar.FrameAdvanceRequested = () => _emulator.StepFrame();
            _toolbar.IsTurboActive = () => _turboActive;

            // Save states: the slot manager plus a GL-backed thumbnail cache for the slot menu.
            _saveStateManager = new SaveStateManager(_emulator, _config);
            _thumbnailCache = new ThumbnailCache(_gl, _saveStateManager);
            _toolbar.StateManager = _saveStateManager;
            _toolbar.GetSlotThumbnail = slot => _thumbnailCache.GetTextureForSlot(slot);

            // Apply the persisted audio/video settings to the freshly-built emulator and renderer.
            RuntimeConfig.ApplyAudio(_config, _emulator.Apu);
            RuntimeConfig.ApplyVideo(_config, _emulator.Ppu, _renderer);

            // Wire up the events to our class methods
            _window.Load += OnLoad;
            _window.Update += OnUpdate;
            _window.Render += OnRender;
            _window.FileDrop += OnFileDrop;
            _window.FocusChanged += OnFocusChanged;
            _window.Resize += OnWindowResize;
        }

        private void OnLoad()
        {
            // This is called when the window is ready
            _window.MakeCurrent();
            _window.FramebufferResize += OnFramebufferResize;
        }

        private void OnFramebufferResize(Vector2D<int> newSize)
        {
            _gl.Viewport(newSize);
        }

        /// <summary>Remembers the window size so it can be restored on the next launch.</summary>
        private void OnWindowResize(Vector2D<int> newSize)
        {
            if (newSize.X > 0 && newSize.Y > 0)
            {
                _config.WindowWidth = newSize.X;
                _config.WindowHeight = newSize.Y;
            }
        }

        /// <summary>Loads a Game Boy ROM dragged onto the window.</summary>
        private void OnFileDrop(string[] paths)
        {
            foreach (string path in paths)
            {
                string extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension is ".gb" or ".gbc" or ".sgb")
                {
                    LoadRomFromUi(path);
                    return;
                }
            }
        }

        /// <summary>Optionally pauses the emulator while the window is in the background.</summary>
        private void OnFocusChanged(bool focused)
        {
            if (!_config.General.PauseOnFocusLoss) return;

            if (!focused)
            {
                // Only pause (and remember we did) if it isn't already paused by the user.
                if (!_emulator.IsPaused)
                {
                    _emulator.Cpu.Pause();
                    _pausedByFocusLoss = true;
                }
            }
            else if (_pausedByFocusLoss)
            {
                _emulator.Cpu.Continue();
                _pausedByFocusLoss = false;
            }
        }

        private void OnUpdate(double delta)
        {
            // Select this window's ImGui context so the WantCaptureKeyboard query below reads our IO,
            // not the debug window's (whichever rendered last leaves its context current).
            _imGuiController.MakeCurrent();

            // Default to no turbo each frame; HandleHotkeys turns it on while the key is held. Resetting
            // here means turbo can never get "stuck" on if focus moves to a text field.
            _turboActive = false;

            // Feed keyboard/gamepad state to the joypad — unless ImGui is currently capturing the
            // keyboard (e.g. the user is typing in a settings field or rebinding a key), in which case
            // the keystrokes belong to the UI, not the game.
            if (!ImGui.GetIO().WantCaptureKeyboard)
            {
                _inputManager.Update(_inputContext, _emulator.Joypad);
                HandleHotkeys();
            }

            // Keep the effective mute in sync: the user's mute setting, plus an optional mute while
            // fast-forwarding (so turbo doesn't produce chipmunk audio).
            _emulator.Apu.Muted = _config.Audio.Muted || (_turboActive && _config.General.MuteDuringTurbo);
        }

        /// <summary>Acts on the configurable global hotkeys (save/load state, slot select, debug, …).</summary>
        private void HandleHotkeys()
        {
            HotkeysConfig hotkeys = _config.Hotkeys;

            if (_inputManager.WasKeyJustPressed(hotkeys.SaveState)) _saveStateManager.QuickSave();
            if (_inputManager.WasKeyJustPressed(hotkeys.LoadState)) _saveStateManager.QuickLoad();
            if (_inputManager.WasKeyJustPressed(hotkeys.ToggleDebug)) _toolbar.ToggleDebugWindow?.Invoke();
            if (_inputManager.WasKeyJustPressed(hotkeys.PausePlay)) TogglePause();
            if (_inputManager.WasKeyJustPressed(hotkeys.Reset)) _openResetConfirm = true;

            // Turbo is a hold: the main loop reads TurboActive and stops sleeping between frames.
            _turboActive = _inputManager.IsKeyDown(hotkeys.Turbo);

            // Frame-advance: while paused, each press runs exactly one frame.
            if (_emulator.IsPaused && _inputManager.WasKeyJustPressed(hotkeys.FrameAdvance))
            {
                _emulator.StepFrame();
            }

            // Number keys 0-9 pick the active quicksave slot.
            for (int i = 0; i < SlotKeys.Length; i++)
            {
                if (_inputManager.WasKeyJustPressed(SlotKeys[i])) _saveStateManager.CurrentSlot = i;
            }
        }

        private void TogglePause()
        {
            if (_emulator.IsPaused) _emulator.Cpu.Continue();
            else _emulator.Cpu.Pause();
        }

        /// <summary>Cycles the normal-speed multiplier 0.5x → 1x → 2x and persists the choice.</summary>
        private void CycleSpeed()
        {
            double current = _config.General.SpeedMultiplier;
            _config.General.SpeedMultiplier = current < 0.99 ? 1.0 : (current < 1.99 ? 2.0 : 0.5);
            ConfigStore.Save(_config);
        }

        private void OnRender(double delta)
        {
            _window.MakeCurrent();

            var framebufferSize = _window.FramebufferSize;
            uint fullWidth = (uint)Math.Max(1, framebufferSize.X);
            uint fullHeight = (uint)Math.Max(1, framebufferSize.Y);

            // Clear the whole window first.
            _gl.Viewport(0, 0, fullWidth, fullHeight);
            _gl.ClearColor(0.1f, 0.1f, 0.15f, 1.0f);
            _gl.Clear(ClearBufferMask.ColorBufferBit);

            UpdateFps(delta);

            // We run two ImGui controllers (this window and the debug window), and ImGui's "current
            // context" is process-global, so we must select ours before touching any ImGui state —
            // otherwise the toolbar would be built against the other window's context and mispositioned.
            _imGuiController.MakeCurrent();

            // Begin the ImGui frame so the toolbar's size is known before we offset the game viewport.
            _imGuiController.Update((float)delta);

            // Reserve the top strip for the toolbar by shrinking the game's available area. We express
            // the strip as a fraction of the display height so it's correct regardless of DPI/scaling.
            var imguiViewport = ImGui.GetMainViewport();
            float toolbarFraction = imguiViewport.Size.Y > 0 ? Toolbar.HeightPoints / imguiViewport.Size.Y : 0f;
            uint toolbarPixels = (uint)(fullHeight * toolbarFraction);
            int gameAreaHeight = Math.Max(1, (int)fullHeight - (int)toolbarPixels);

            // Place the game image within that area, honouring the aspect-ratio / integer-scaling
            // preferences (GL's viewport origin is the bottom-left corner, so the area sits at y=0).
            ComputeGameViewport((int)fullWidth, gameAreaHeight, out int vx, out int vy, out int vw, out int vh);
            _gl.Viewport(vx, vy, (uint)vw, (uint)vh);
            var frameBuffer = _emulator.Ppu.GetFrameBuffer();
            _renderer.Render(frameBuffer);

            // Draw the toolbar and any open dialogs over the full window, then flush ImGui.
            _gl.Viewport(0, 0, fullWidth, fullHeight);
            DrawUi();
            _imGuiController.Render();
        }

        /// <summary>Draws all ImGui UI for the game window: the toolbar plus any open dialogs.</summary>
        private void DrawUi()
        {
            _toolbar.Draw(_fps);
            _fileBrowser.Draw();
            _settingsDialog.Draw();
            DrawResetConfirm();
        }

        /// <summary>
        /// Computes where the game image should be drawn inside the available area (everything below
        /// the toolbar). With both options off the image stretches to fill; otherwise it is centred and
        /// scaled to preserve the 160:144 aspect ratio, optionally snapped to a whole-number scale.
        /// </summary>
        private void ComputeGameViewport(int areaWidth, int areaHeight, out int x, out int y, out int w, out int h)
        {
            bool keepAspect = _config.Video.LockAspectRatio || _config.Video.IntegerScale;
            if (!keepAspect)
            {
                x = 0; y = 0; w = areaWidth; h = areaHeight;
                return;
            }

            const float gbWidth = GameboyConstants.ScreenWidth;
            const float gbHeight = GameboyConstants.ScreenHeight;

            float scale = Math.Min(areaWidth / gbWidth, areaHeight / gbHeight);
            if (_config.Video.IntegerScale)
            {
                scale = Math.Max(1f, MathF.Floor(scale));
            }

            w = (int)(gbWidth * scale);
            h = (int)(gbHeight * scale);
            x = (areaWidth - w) / 2;
            y = (areaHeight - h) / 2;
        }

        /// <summary>A confirmation modal guarding the Reset button, since a reset discards progress.</summary>
        private void DrawResetConfirm()
        {
            if (_openResetConfirm)
            {
                ImGui.OpenPopup("Reset?");
                _openResetConfirm = false;
            }

            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(viewport.Pos + viewport.Size * 0.5f, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

            bool open = true;
            if (ImGui.BeginPopupModal("Reset?", ref open, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextUnformatted("Reset the emulator? Any unsaved in-game progress will be lost.");
                ImGui.Spacing();
                if (ImGui.Button("Reset", new Vector2(120, 0)))
                {
                    _emulator.Reset();
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(120, 0)))
                {
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
        }

        /// <summary>
        /// Loads a ROM chosen through the UI (file browser or recents), records it in the recent list,
        /// and persists the updated config. Load failures are logged rather than thrown so a bad pick
        /// never takes the window down.
        /// </summary>
        private void LoadRomFromUi(string path)
        {
            try
            {
                _emulator.LoadRom(path);
                _config.AddRecentRom(path);
                ConfigStore.Save(_config);
                Log.Information("Loaded ROM: {Path}", path);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load ROM: {Path}", path);
            }
        }

        /// <summary>Tracks a smoothed FPS and refreshes the window title a couple of times a second.</summary>
        private void UpdateFps(double delta)
        {
            if (delta > 0)
            {
                double instantaneous = 1.0 / delta;
                // Exponential moving average so the number doesn't jitter every frame.
                _fps = _fps <= 0 ? instantaneous : _fps * 0.9 + instantaneous * 0.1;
            }

            if (++_titleUpdateCounter >= 30)
            {
                _titleUpdateCounter = 0;
                _window.Title = $"{_emulator.CurrentRomName} — {_fps:0} FPS";
            }
        }

        public void Dispose()
        {
            // Unsubscribe from window events
            if (_window != null)
            {
                _window.Load -= OnLoad;
                _window.Update -= OnUpdate;
                _window.Render -= OnRender;
                _window.FramebufferResize -= OnFramebufferResize;
                _window.FileDrop -= OnFileDrop;
                _window.FocusChanged -= OnFocusChanged;
                _window.Resize -= OnWindowResize;
            }

            _window?.MakeCurrent();
            _thumbnailCache?.Dispose();
            _imGuiController?.Dispose();
            _renderer?.Dispose();
            _inputContext?.Dispose();
            _gl?.Dispose();
            _window?.Dispose();
        }
    }
}
