// DebugWindow.cs
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;

namespace GameboySharp
{
    internal class DebugWindow : IDisposable
    {
        private readonly IWindow _window;
        private readonly Emulator _emulator;
        private ImGuiController _imGuiController;
        private GL _gl;

        // State for UI controls
        private bool _autoScroll = true;
        private int _selectedVramBank = 0;
        private int _selectedGbcPalette = 0;

        // OpenGL Textures for sprite/tile viewers
        private uint[] _spriteTextureIds = new uint[40];
        private uint[] _tileTextureIds = new uint[384 * 2]; // For both GBC VRAM banks

        private bool[] _spriteDirty = new bool[40];
        private byte[][] _cachedSpriteData = new byte[40][];

        public IWindow SilkWindow => _window;

        public DebugWindow(Emulator emulator)
        {
            _emulator = emulator;

            var options = WindowOptions.Default;
            options.API = new GraphicsAPI(ContextAPI.OpenGL, new APIVersion(3, 3));
            options.Title = "Debugger";
            options.Size = new Vector2D<int>(1200, 1000);
            options.VSync = false;

            _window = Window.Create(options);
            _window.Initialize();

            _gl = _window.CreateOpenGL();
            _imGuiController = new ImGuiController(_gl, _window, _window.CreateInput());
            _window.FramebufferResize += s => _gl.Viewport(s);

            _window.Load += OnLoad;
            _window.Render += OnRender;

            // Pre-generate all the small textures needed for the VRAM and sprite viewers
            InitializeGpuTextures();
        }

        private void OnLoad()
        {
            _window.MakeCurrent();
        }

        private void OnRender(double delta)
        {
            _window.MakeCurrent();
            _gl.ClearColor(0.2f, 0.2f, 0.25f, 1.0f);
            _gl.Clear(ClearBufferMask.ColorBufferBit);

            _imGuiController.Update((float)delta);

            // Enable Docking and create the main viewport dock space
            // This turns the entire window into a dockable area for other windows.
            ImGui.DockSpaceOverViewport();

            DrawCpuPanel();
            DrawPPUPanel();
            DrawSerialPanel();

            _imGuiController.Render();
        }

        private void DrawCpuPanel()
        {
            // --- Window 1: CPU State, Controls, and Memory ---
            if (ImGui.Begin("CPU & System"))
            {
                // == CPU Controls ==
                if (ImGui.CollapsingHeader("Controls", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    if (ImGui.Button("Step")) _emulator.Cpu.StepInstruction();
                    ImGui.SameLine();
                    if (ImGui.Button("Continue")) _emulator.Cpu.Continue();
                    ImGui.SameLine();
                    if (ImGui.Button("Pause")) _emulator.Cpu.Pause();
                    ImGui.SameLine();
                    ImGui.Text("Status: " + (_emulator.Cpu.IsPaused ? "PAUSED" : "RUNNING"));
                }

                // == CPU Registers & Flags ==
                if (ImGui.CollapsingHeader("Registers & Flags", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.Columns(2, "reg_columns", false);
                    ImGui.Text("PC"); ImGui.NextColumn(); ImGui.Text($"0x{_emulator.Cpu.PC:X4}"); ImGui.NextColumn();
                    ImGui.Text("SP"); ImGui.NextColumn(); ImGui.Text($"0x{_emulator.Cpu.SP:X4}"); ImGui.NextColumn();
                    ImGui.Separator();
                    ImGui.Text("A"); ImGui.NextColumn(); ImGui.Text($"0x{_emulator.Cpu.A:X2}"); ImGui.NextColumn();
                    ImGui.Text("F"); ImGui.NextColumn(); ImGui.Text($"0x{_emulator.Cpu.F:X2}"); ImGui.NextColumn();
                    ImGui.Text("BC"); ImGui.NextColumn(); ImGui.Text($"0x{_emulator.Cpu.BC:X4}"); ImGui.NextColumn();
                    ImGui.Text("DE"); ImGui.NextColumn(); ImGui.Text($"0x{_emulator.Cpu.DE:X4}"); ImGui.NextColumn();
                    ImGui.Text("HL"); ImGui.NextColumn(); ImGui.Text($"0x{_emulator.Cpu.HL:X4}"); ImGui.NextColumn();
                    ImGui.Columns(1);
                    ImGui.Separator();
                    ImGui.Text($"Flags: [Z:{(_emulator.Cpu.FlagZ ? 1 : 0)}] [N:{(_emulator.Cpu.FlagN ? 1 : 0)}] [H:{(_emulator.Cpu.FlagH ? 1 : 0)}] [C:{(_emulator.Cpu.FlagC ? 1 : 0)}]");
                }

                // == Memory Viewer (around PC) ==
                if (ImGui.CollapsingHeader("Memory View @ PC"))
                {
                    ushort startAddr = (ushort)Math.Max(0, _emulator.Cpu.PC - 8);
                    for (int i = 0; i < 16; i++)
                    {
                        ushort addr = (ushort)(startAddr + i);
                        if (addr < startAddr) continue; // Handle wraparound
                        byte value = _emulator.Mmu.ReadByte(addr);

                        if (addr == _emulator.Cpu.PC)
                        {
                            ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1.0f, 1.0f, 0.4f, 1.0f));
                            ImGui.Text($"0x{addr:X4}: 0x{value:X2} <- PC");
                            ImGui.PopStyleColor();
                        }
                        else
                        {
                            ImGui.Text($"0x{addr:X4}: 0x{value:X2}");
                        }
                    }
                }

                // == Cartridge Info ==
                if (ImGui.CollapsingHeader("Cartridge Info"))
                {
                    ImGui.TextWrapped(_emulator.Mmu.GetMbcInfo());
                }

                // == Last Opcode executed Info ==
                if (ImGui.CollapsingHeader("Last Opcode Executed"))
                {
                    ImGui.TextWrapped($"Op: {_emulator.Cpu.LastOpcodeLog}");
                    ImGui.TextWrapped($"Desc: {_emulator.Cpu.LastExecutedOpcode.Description}");
                    ImGui.Text($"Cycles: {_emulator.Cpu.LastExecutedOpcode.Cycles} | Bytes: {_emulator.Cpu.LastExecutedOpcode.Bytes}");
                }

                ImGui.End();
            }
        }

        private unsafe void DrawPPUPanel()
        { 
            // --- Window 2: PPU Viewers (VRAM & OAM) ---
            // This window uses a tab bar to switch between the tile and sprite viewers.
            if (ImGui.Begin("PPU Viewer"))
            {
                // == CPU Controls ==
                if (ImGui.CollapsingHeader("PPU Controls", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    // "Enable All" checkbox
                    bool allEnabled = _emulator.Ppu.RenderBackground && _emulator.Ppu.RenderWindow && _emulator.Ppu.RenderSprites;
                    if (ImGui.Checkbox("Enable All", ref allEnabled))
                    {
                        _emulator.Ppu.RenderBackground = allEnabled;
                        _emulator.Ppu.RenderWindow = allEnabled;
                        _emulator.Ppu.RenderSprites = allEnabled;
                    }

                    ImGui.Separator();

                    // Individual toggles
                    ImGui.Checkbox("Render Background", ref _emulator.Ppu.RenderBackground);
                    ImGui.Checkbox("Render Window", ref _emulator.Ppu.RenderWindow);
                    ImGui.Checkbox("Render Sprites", ref _emulator.Ppu.RenderSprites);
                }

                if (ImGui.BeginTabBar("PPU_Tabs"))
                    {
                        // == VRAM Tile Viewer Tab ==
                        if (ImGui.BeginTabItem("VRAM Tiles"))
                        {
                            // --- VRAM VIEWER GBC SUPPORT ---
                            if (_emulator.Mmu.IsGameBoyColor)
                            {
                                ImGui.SliderInt("VRAM Bank", ref _selectedVramBank, 0, 1);
                                ImGui.SliderInt("Palette Index", ref _selectedGbcPalette, 0, 7);
                                ImGui.Separator();
                            }

                            // Configure the grid layout
                            int tilesPerRow = 16;
                            var tileSize = new System.Numerics.Vector2(32, 32); // Display size for each 8x8 tile
                            float padding = 4.0f;

                            // --- Pre-fetch GBC palettes if needed ---
                            uint[,] gbcPalettes = new uint[8, 4];
                            if (_emulator.Mmu.IsGameBoyColor)
                            {
                                byte[] rawPaletteData = new byte[64];
                                for (byte i = 0; i < 64; i++)
                                {
                                    _emulator.Mmu.WriteByte(0xFF68, i); // Set BCPS to index
                                    rawPaletteData[i] = _emulator.Mmu.ReadByte(0xFF69); // Read from BCPD
                                }

                                for (int p = 0; p < 8; p++) // 8 palettes
                                {
                                    for (int c = 0; c < 4; c++) // 4 colors per palette
                                    {
                                        int offset = p * 8 + c * 2;
                                        ushort gbcColor = (ushort)(rawPaletteData[offset] | (rawPaletteData[offset + 1] << 8));
                                        gbcPalettes[p, c] = _emulator.Ppu.Convert15BitTo32Bit(gbcColor);
                                    }
                                }
                            }

                            for (int tileIndex = 0; tileIndex < 384; tileIndex++)
                            {
                                uint[] pixelData = new uint[64]; // 8x8 pixels
                                ushort baseAddress = (ushort)(0x8000 + (tileIndex * 16));

                                for (int y = 0; y < 8; y++)
                                {
                                    // In GBC mode, we must be able to specify the VRAM bank
                                    byte lsb = _emulator.Mmu.IsGameBoyColor ? _emulator.Mmu.ReadVram((ushort)(baseAddress + (y * 2)), _selectedVramBank) : _emulator.Mmu.ReadByte((ushort)(baseAddress + (y * 2)));
                                    byte msb = _emulator.Mmu.IsGameBoyColor ? _emulator.Mmu.ReadVram((ushort)(baseAddress + (y * 2) + 1), _selectedVramBank) : _emulator.Mmu.ReadByte((ushort)(baseAddress + (y * 2) + 1));

                                    for (int x = 0; x < 8; x++)
                                    {
                                        // Bits are arranged from left to right (7 to 0)
                                        int bit1 = (lsb >> (7 - x)) & 1;
                                        int bit2 = (msb >> (7 - x)) & 1;
                                        int colorIndex = (bit2 << 1) | bit1;

                                        if (_emulator.Mmu.IsGameBoyColor)
                                        {
                                            pixelData[y * 8 + x] = gbcPalettes[_selectedGbcPalette, colorIndex];
                                        }
                                        else // DMG Mode
                                        {
                                            byte bgp = _emulator.Mmu.ReadByte(0xFF47);
                                            int shadeBits = (bgp >> (colorIndex * 2)) & 0b11;
                                            pixelData[y * 8 + x] = Ppu._dmgColors[shadeBits];
                                        }
                                    }
                                }

                                // Convert uint[] frame buffer to byte[] for proper RGBA upload
                                var byteBuffer = new byte[pixelData.Length * 4];
                                for (int i = 0; i < pixelData.Length; i++)
                                {
                                    uint color = pixelData[i];
                                    int byteIndex = i * 4;

                                    // Correctly unpack the AABBGGRR uint to an RGBA byte array.
                                    byteBuffer[byteIndex] = (byte)(color & 0xFF);         // R
                                    byteBuffer[byteIndex + 1] = (byte)((color >> 8) & 0xFF);  // G
                                    byteBuffer[byteIndex + 2] = (byte)((color >> 16) & 0xFF); // B
                                    byteBuffer[byteIndex + 3] = (byte)((color >> 24) & 0xFF); // A
                                }

                                // Use a unique texture ID for each bank/tile combination to avoid conflicts
                                int textureIdIndex = tileIndex + (_selectedVramBank * 384);
                                _gl.BindTexture(TextureTarget.Texture2D, _tileTextureIds[textureIdIndex]);
                                unsafe
                                {
                                    fixed (byte* p = byteBuffer)
                                    {
                                        _gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 8, 8,
                                            PixelFormat.Rgba, PixelType.UnsignedByte, p);
                                    }
                                }

                                // Draw the tile image
                                ImGui.Image((IntPtr)_tileTextureIds[textureIdIndex], tileSize);

                                // Arrange in a grid
                                if ((tileIndex + 1) % tilesPerRow != 0)
                                {
                                    ImGui.SameLine(0, padding);
                                }
                            }
                            ImGui.EndTabItem();
                        }

                        // == OAM Sprite Viewer Tab ==
                        if (ImGui.BeginTabItem("OAM Sprites"))
                        {
                            var imageSize = new System.Numerics.Vector2(32, 32);

                            for (int i = 0; i < 40; i++)
                            {
                                // Safety check
                                if (_spriteTextureIds == null) break;

                                // --- TEMPORARY DEBUGGING CHANGE ---
                                // Always get the current sprite data and upload it, bypassing the cache check.
                                byte[] currentSpriteData = _emulator.Ppu.GetSpriteRgba(i);

                                _gl.BindTexture(TextureTarget.Texture2D, _spriteTextureIds[i]);
                                fixed (byte* p = currentSpriteData)
                                {
                                    _gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 8, 8,
                                        PixelFormat.Rgba, PixelType.UnsignedByte, p);
                                }
                                // --- END OF CHANGE ---

                                ImGui.Image((IntPtr)_spriteTextureIds[i], imageSize);
                                if ((i + 1) % 8 != 0) ImGui.SameLine();
                            }
                            ImGui.EndTabItem();
                        }

                        ImGui.EndTabBar();
                    }
                ImGui.End();
            }
        }

        private void DrawSerialPanel()
        {
            if (ImGui.Begin("Serial Log"))
            {
                if (ImGui.Button("Clear")) _emulator.SerialLog.Clear();
                ImGui.SameLine();
                ImGui.Checkbox("Auto-scroll", ref _autoScroll);
                ImGui.Separator();
                
                ImGui.BeginChild("ScrollingRegion", new System.Numerics.Vector2(0, 0), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);
                ImGui.TextUnformatted(_emulator.SerialLog.ToString());
                if (_autoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
                {
                    ImGui.SetScrollHereY(1.0f);
                }
                ImGui.EndChild();
                ImGui.End();
            }
        }

        private unsafe void InitializeGpuTextures()
        {
            // This is called once on load to create the texture objects on the GPU
            // We will just update their contents each frame in the render loop.

            // Initialize sprite viewer arrays
            for (int i = 0; i < 40; i++)
            {
                _cachedSpriteData[i] = new byte[8 * 8 * 4]; // Initialize with empty data
                _spriteDirty[i] = true; // Mark all sprites as dirty initially
            }

            // Create 40 textures for our sprite viewer
            _gl.GenTextures(40, _spriteTextureIds);
            for (int i = 0; i < 40; i++)
            {
                _gl.BindTexture(TextureTarget.Texture2D, _spriteTextureIds[i]);
                // Set texture parameters (e.g., filtering)
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);

                // Allocate storage for an 8x8 RGBA texture
                unsafe
                {
                    _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, 8, 8, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
                }
            }

            // Generate textures for VRAM tile viewer (covers both VRAM banks for GBC)
            _gl.GenTextures(384 * 2, _tileTextureIds);
            for (int i = 0; i < 384 * 2; i++)
            {
                _gl.BindTexture(TextureTarget.Texture2D, _tileTextureIds[i]);
                // Set texture parameters (e.g., filtering)
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);

                // Allocate storage for an 8x8 RGBA texture
                unsafe
                {
                    _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, 8, 8, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
                }
            }
        }

        public void Dispose()
        {
            // Unsubscribe from window events
            if (_window != null)
            {
                _window.Load -= OnLoad;
                _window.Render -= OnRender;
                _window.FramebufferResize -= s => _gl.Viewport(s);
            }
            //TODO: fix
            //_gl.DeleteTextures(_tileTextureIds.Length, _tileTextureIds);
            _imGuiController?.Dispose();
            _gl?.Dispose();
            _window?.Dispose();
        }
    }
}