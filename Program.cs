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

        Log.Information("Initializing Emulator...");
        using var emulator = new Emulator();
        
        try
        {
            // Load the ROM. Change this path to your ROM file.
            emulator.LoadRom("YOUR_ROM_HERE");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load ROM.");
            return;
        }

        Log.Information("Creating windows...");
        using var gameWindow = new GameWindow(emulator);
        using var debugWindow = new DebugWindow(emulator);

        // Link the windows so closing one closes the other
        gameWindow.SilkWindow.Closing += () => debugWindow.SilkWindow.Close();
        debugWindow.SilkWindow.Closing += () => gameWindow.SilkWindow.Close();
        
        Log.Information("Starting main loop...");
        var frameTimer = Stopwatch.StartNew();
        const double frameTimeMs = 1000.0 / 60.0; // Target 60 FPS

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

            // Render both windows
            gameWindow.SilkWindow.DoRender();
            debugWindow.SilkWindow.DoRender();
            
            // Frame rate limiting to prevent 100% CPU usage
            var elapsed = frameTimer.Elapsed.TotalMilliseconds;
            var sleepTime = (int)(frameTimeMs - elapsed);
            if (sleepTime > 0)
            {
                Thread.Sleep(sleepTime);
            }
        }

        Log.Information("Closing application.");
    }
}