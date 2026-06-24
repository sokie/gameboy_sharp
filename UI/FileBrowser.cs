using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Serilog;

namespace GameboySharp
{
    /// <summary>
    /// An in-app ImGui file browser modal for picking a ROM. We use this instead of a native OS file
    /// dialog because it works identically on every platform and lives entirely inside the existing
    /// ImGui setup — no second windowing/event system to coexist with Silk's GLFW backend, and none of
    /// the main-thread constraints native dialogs impose (which are especially fiddly on macOS).
    ///
    /// It navigates directories, lists Game Boy ROMs (filtered by extension), and reports the chosen
    /// file through <see cref="OnRomChosen"/>.
    /// </summary>
    internal class FileBrowser
    {
        private static readonly string[] RomExtensions = { ".gb", ".gbc", ".sgb" };

        private bool _isOpen;
        private bool _shouldOpenPopup;
        private string _currentDirectory;
        private string? _selectedFile;

        /// <summary>Invoked with the absolute path when the user confirms a ROM choice.</summary>
        public Action<string>? OnRomChosen;

        public FileBrowser(string? startDirectory = null)
        {
            _currentDirectory = ResolveStartDirectory(startDirectory);
        }

        /// <summary>Opens the browser, optionally starting in a specific directory.</summary>
        public void Open(string? startDirectory = null)
        {
            _currentDirectory = ResolveStartDirectory(startDirectory ?? _currentDirectory);
            _selectedFile = null;
            _isOpen = true;
            _shouldOpenPopup = true;
        }

        /// <summary>
        /// Draws the modal for this frame (a no-op when closed). Call this between the ImGui frame's
        /// Update and Render, like the rest of the UI.
        /// </summary>
        public void Draw()
        {
            if (!_isOpen) return;

            if (_shouldOpenPopup)
            {
                ImGui.OpenPopup("Open ROM");
                _shouldOpenPopup = false;
            }

            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(viewport.Pos + viewport.Size * 0.5f, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
            ImGui.SetNextWindowSize(new Vector2(640, 460), ImGuiCond.Appearing);

            bool stayOpen = true;
            if (ImGui.BeginPopupModal("Open ROM", ref stayOpen))
            {
                DrawNavigationBar();
                ImGui.Separator();
                DrawEntryList();
                ImGui.Separator();
                DrawFooter();
                ImGui.EndPopup();
            }

            // The window's close button (or Escape) clears stayOpen.
            if (!stayOpen) Close();
        }

        private void DrawNavigationBar()
        {
            if (ImGui.Button("Up"))
            {
                NavigateUp();
            }
            ImGui.SameLine();
            if (ImGui.Button("Home"))
            {
                _currentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                _selectedFile = null;
            }
            ImGui.SameLine();
            ImGui.TextUnformatted(_currentDirectory);
        }

        private void DrawEntryList()
        {
            // Leave room at the bottom for the footer (selected-file text + buttons).
            float footerHeight = ImGui.GetFrameHeightWithSpacing() * 2;
            ImGui.BeginChild("entries", new Vector2(0, -footerHeight), ImGuiChildFlags.Borders);

            foreach (string dir in GetSubdirectories())
            {
                string name = Path.GetFileName(dir);
                // Folders are shown with a trailing slash; double-clicking enters them.
                if (ImGui.Selectable($"[ {name} ]/", false, ImGuiSelectableFlags.AllowDoubleClick))
                {
                    if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    {
                        _currentDirectory = dir;
                        _selectedFile = null;
                    }
                }
            }

            foreach (string file in GetRomFiles())
            {
                string name = Path.GetFileName(file);
                bool selected = string.Equals(file, _selectedFile, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(name, selected, ImGuiSelectableFlags.AllowDoubleClick))
                {
                    _selectedFile = file;
                    // Double-click is the quick "open this one now" gesture.
                    if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    {
                        Choose(file);
                    }
                }
            }

            ImGui.EndChild();
        }

        private void DrawFooter()
        {
            ImGui.TextUnformatted(_selectedFile is null ? "No file selected"
                                                         : $"Selected: {Path.GetFileName(_selectedFile)}");

            bool hasSelection = _selectedFile != null;
            if (!hasSelection) ImGui.BeginDisabled();
            if (ImGui.Button("Open", new Vector2(120, 0)) && _selectedFile != null)
            {
                Choose(_selectedFile);
            }
            if (!hasSelection) ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0)))
            {
                Close();
            }
        }

        private void Choose(string path)
        {
            Close();
            OnRomChosen?.Invoke(path);
        }

        private void Close()
        {
            _isOpen = false;
            ImGui.CloseCurrentPopup();
        }

        private void NavigateUp()
        {
            try
            {
                var parent = Directory.GetParent(_currentDirectory);
                if (parent != null)
                {
                    _currentDirectory = parent.FullName;
                    _selectedFile = null;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "FileBrowser: could not navigate up from {Dir}", _currentDirectory);
            }
        }

        /// <summary>Subdirectories of the current folder, sorted, with unreadable folders skipped.</summary>
        private IEnumerable<string> GetSubdirectories()
        {
            try
            {
                return Directory.GetDirectories(_currentDirectory)
                                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "FileBrowser: could not list directories in {Dir}", _currentDirectory);
                return Enumerable.Empty<string>();
            }
        }

        /// <summary>ROM files in the current folder (filtered by extension), sorted.</summary>
        private IEnumerable<string> GetRomFiles()
        {
            try
            {
                return Directory.GetFiles(_currentDirectory)
                                .Where(f => RomExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "FileBrowser: could not list files in {Dir}", _currentDirectory);
                return Enumerable.Empty<string>();
            }
        }

        /// <summary>Picks a sensible, existing directory to start in.</summary>
        private static string ResolveStartDirectory(string? candidate)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
            {
                return candidate!;
            }

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Directory.Exists(home) ? home : Directory.GetCurrentDirectory();
        }
    }
}
