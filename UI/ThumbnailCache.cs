using System;
using Silk.NET.OpenGL;

namespace GameboySharp
{
    /// <summary>
    /// Lazily turns save-state slot thumbnails into OpenGL textures for display in the toolbar's slot
    /// menu. Textures are created on first use and re-uploaded only when a slot's file changes (tracked
    /// by its last-write time), so an open menu costs nothing extra frame to frame.
    /// </summary>
    internal class ThumbnailCache : IDisposable
    {
        private readonly GL _gl;
        private readonly SaveStateManager _manager;

        private readonly uint[] _textures = new uint[SaveStateManager.SlotCount];
        private readonly DateTime[] _uploadedFor = new DateTime[SaveStateManager.SlotCount];

        public ThumbnailCache(GL gl, SaveStateManager manager)
        {
            _gl = gl;
            _manager = manager;
        }

        /// <summary>
        /// Returns the ImGui texture handle for a slot's thumbnail, or 0 if the slot is empty or has no
        /// readable thumbnail. Safe to call every frame the slot menu is open.
        /// </summary>
        public nint GetTextureForSlot(int slot)
        {
            DateTime? timestamp = _manager.Timestamp(slot);
            if (timestamp == null)
            {
                return 0; // empty slot
            }

            // Reuse the cached texture unless the slot's file has changed since we last uploaded it.
            if (_textures[slot] != 0 && _uploadedFor[slot] == timestamp.Value)
            {
                return (nint)_textures[slot];
            }

            SaveStateThumbnail? thumbnail = _manager.ReadThumbnail(slot);
            if (thumbnail is not { } thumb || thumb.Rgba.Length != thumb.Width * thumb.Height * 4)
            {
                return 0;
            }

            Upload(slot, thumb);
            _uploadedFor[slot] = timestamp.Value;
            return (nint)_textures[slot];
        }

        private unsafe void Upload(int slot, SaveStateThumbnail thumb)
        {
            if (_textures[slot] == 0)
            {
                _textures[slot] = _gl.GenTexture();
                _gl.BindTexture(TextureTarget.Texture2D, _textures[slot]);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
            }
            else
            {
                _gl.BindTexture(TextureTarget.Texture2D, _textures[slot]);
            }

            fixed (byte* pixels = thumb.Rgba)
            {
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba,
                    (uint)thumb.Width, (uint)thumb.Height, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            }
        }

        public void Dispose()
        {
            foreach (uint texture in _textures)
            {
                if (texture != 0) _gl.DeleteTexture(texture);
            }
        }
    }
}
