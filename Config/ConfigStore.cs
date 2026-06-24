using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace GameboySharp
{
    /// <summary>
    /// Loads and saves <see cref="EmulatorConfig"/> as a human-readable JSON file under the user's
    /// application-data folder (e.g. <c>%AppData%/GameboySharp/config.json</c> on Windows, or
    /// <c>~/.config/GameboySharp/config.json</c> on Linux/macOS).
    ///
    /// The store is intentionally forgiving: a missing or unreadable file simply yields a fresh set
    /// of defaults rather than crashing the emulator, because losing your key bindings should never
    /// stop a game from booting.
    /// </summary>
    public static class ConfigStore
    {
        private const string AppFolderName = "GameboySharp";
        private const string ConfigFileName = "config.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            // Persist enums (keys, buttons, palette names) as readable strings instead of integers,
            // so the file is hand-editable and stays valid if enum values are ever reordered.
            Converters = { new JsonStringEnumConverter() },
        };

        /// <summary>The absolute path the config is read from and written to.</summary>
        public static string ConfigFilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppFolderName,
                ConfigFileName);

        /// <summary>
        /// Reads the config from disk, returning a fresh default config if the file is missing or
        /// cannot be parsed. Never throws.
        /// </summary>
        public static EmulatorConfig Load() => Load(ConfigFilePath);

        /// <summary>Writes the config to its standard location. See <see cref="Save(EmulatorConfig, string)"/>.</summary>
        public static void Save(EmulatorConfig config) => Save(config, ConfigFilePath);

        /// <summary>Loads the config from a specific path (the path-explicit form used by tests).</summary>
        internal static EmulatorConfig Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    Log.Information("No config file found at {Path}; using defaults.", path);
                    return new EmulatorConfig();
                }

                string json = File.ReadAllText(path);
                var config = JsonSerializer.Deserialize<EmulatorConfig>(json, JsonOptions);

                // Deserialize can return null for a literal "null" file; guard against it.
                return config ?? new EmulatorConfig();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load config from {Path}; using defaults.", path);
                return new EmulatorConfig();
            }
        }

        /// <summary>
        /// Writes the config to <paramref name="path"/>, creating the folder if needed. Failures are
        /// logged but swallowed — a settings save should never take down the emulator.
        /// </summary>
        internal static void Save(EmulatorConfig config, string path)
        {
            try
            {
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonSerializer.Serialize(config, JsonOptions);
                File.WriteAllText(path, json);
                Log.Debug("Saved config to {Path}.", path);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to save config to {Path}.", path);
            }
        }
    }
}
