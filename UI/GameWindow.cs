// GameWindow.cs
using GameboySharp;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;


namespace GameboySharp
{
    internal class GameWindow : IDisposable
    {
        private readonly IWindow _window;
        private readonly Emulator _emulator;
        private ScreenRenderer _renderer;
        private GL _gl;
        private IKeyboard _keyboard;

        // We expose the underlying IWindow so the main loop can control it
        public IWindow SilkWindow => _window;

        public GameWindow(Emulator emulator)
        {
            _emulator = emulator;

            var options = WindowOptions.Default;
            options.API = new GraphicsAPI(ContextAPI.OpenGL, new APIVersion(3, 3));
            options.Title = "Game Boy Emulator";
            options.Size = new Vector2D<int>(GameboyConstants.ScreenWidth * 4, GameboyConstants.ScreenHeight * 4);
            options.VSync = false; // We handle frame pacing manually in the main loop

            _window = Window.Create(options);
            _window.Initialize();
            _gl = _window.CreateOpenGL();
            // Get the primary keyboard for input
            _keyboard = _window.CreateInput().Keyboards[0];
            
            // Create our dedicated renderer
            _renderer = new ScreenRenderer(_gl);
            _renderer.Load();

            // Wire up the events to our class methods
            _window.Load += OnLoad;
            _window.Update += OnUpdate;
            _window.Render += OnRender;
        }

        private void OnLoad()
        {
            // This is called when the window is ready
            _window.MakeCurrent();
            _window.FramebufferResize += s => _gl.Viewport(s);
        }

        private void OnUpdate(double delta)
        {
            // This is the ideal place for non-rendering logic, like input handling
            _emulator.UpdateInput(_keyboard);
        }

        private void OnRender(double delta)
        {
            _window.MakeCurrent();
            _gl.ClearColor(0.1f, 0.1f, 0.15f, 1.0f);
            _gl.Clear(ClearBufferMask.ColorBufferBit);

            var frameBuffer = _emulator.Ppu.GetFrameBuffer();
            _renderer.Render(frameBuffer);
        }

        public void Dispose()
        {
            // Unsubscribe from window events
            if (_window != null)
            {
                _window.Load -= OnLoad;
                _window.Update -= OnUpdate;
                _window.Render -= OnRender;
                _window.FramebufferResize -= s => _gl.Viewport(s);
            }
            
            _renderer?.Dispose();
            _gl?.Dispose();
            _window?.Dispose();
        }
    }
}