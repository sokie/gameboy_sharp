// Program.cs
using GameboySharp;
using Serilog;
using Serilog.Events;
using System.Diagnostics;

public class Program
{
    public static void Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            //.MinimumLevel.Debug()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}", restrictedToMinimumLevel: LogEventLevel.Information)
            .WriteTo.File(
                path: "logs/gameboysharp-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        // Load persisted settings (key bindings, audio/video preferences, recent ROMs). Missing or
        // corrupt config falls back to defaults, so this never blocks startup.
        var config = ConfigStore.Load();

        Log.Information("Initializing Emulator...");
        using var emulator = new Emulator(config);

        // The ROM path is an optional first command-line argument, e.g. `dotnet run -- path/to/game.gb`.
        // Without one (or if loading fails) the emulator simply starts with no cartridge; the user can
        // open a ROM from the toolbar instead of the app refusing to launch.
        if (args.Length > 0)
        {
            try
            {
                emulator.LoadRom(args[0]);
                config.AddRecentRom(args[0]);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to load ROM at '{args[0]}'. You can open a different ROM from the toolbar.");
            }
        }

        Log.Information("Creating windows...");
        using var gameWindow = new GameWindow(emulator, config);
        using var debugWindow = new DebugWindow(emulator);

        // The debug window starts hidden; the toolbar's Debug button shows/hides it on demand.
        debugWindow.SilkWindow.IsVisible = false;

        // Let the toolbar query and toggle the debug window's visibility.
        gameWindow.Toolbar.IsDebugWindowVisible = () => debugWindow.SilkWindow.IsVisible;
        gameWindow.Toolbar.ToggleDebugWindow = () =>
            debugWindow.SilkWindow.IsVisible = !debugWindow.SilkWindow.IsVisible;

        // Closing either window shuts down the whole app. A re-entrancy guard is essential here:
        // Silk fires `Closing` on every `Close()` call, so a naive "each closes the other" pair would
        // recurse forever (game closes debug → debug closes game → …) and overflow the stack.
        bool shuttingDown = false;
        void ShutDownBothWindows()
        {
            if (shuttingDown) return;
            shuttingDown = true;
            gameWindow.SilkWindow.Close();
            debugWindow.SilkWindow.Close();
        }
        gameWindow.SilkWindow.Closing += ShutDownBothWindows;
        debugWindow.SilkWindow.Closing += ShutDownBothWindows;

        Log.Information("Starting main loop...");
        var frameTimer = Stopwatch.StartNew();
        const double frameTimeMs = 1000.0 / GameboyConstants.FramesPerSecond;

        //while(true)
        while (!gameWindow.SilkWindow.IsClosing && !debugWindow.SilkWindow.IsClosing)
        {
            frameTimer.Restart();

            // Process all pending events for both windows
            gameWindow.SilkWindow.DoEvents();
            debugWindow.SilkWindow.DoEvents();

            // The Update event on the GameWindow handles input
            gameWindow.SilkWindow.DoUpdate();

            // Run the emulator for one frame's worth of cycles
            emulator.RunFrame();

            // Render the game window every frame. The debug window does heavy per-frame work (it
            // re-uploads hundreds of tile textures), so only render it while it's actually visible.
            gameWindow.SilkWindow.DoRender();
            if (debugWindow.SilkWindow.IsVisible)
            {
                debugWindow.SilkWindow.DoRender();
            }
            
            // Frame pacing. The target time per frame scales with the configured speed multiplier
            // (e.g. 2x → half the wait), and holding turbo drops pacing entirely to run flat-out.
            if (!gameWindow.TurboActive)
            {
                double speed = Math.Max(0.1, config.General.SpeedMultiplier);
                double targetFrameMs = frameTimeMs / speed;
                var elapsed = frameTimer.Elapsed.TotalMilliseconds;
                var sleepTime = (int)(targetFrameMs - elapsed);
                if (sleepTime > 0)
                {
                    Thread.Sleep(sleepTime);
                }
            }
        }

        Log.Information("Closing application.");

        // Persist any settings changed during the session (e.g. the recent-ROMs list).
        ConfigStore.Save(config);
    }
}