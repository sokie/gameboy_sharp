// ScreenRenderer.cs
using Silk.NET.OpenGL;

namespace GameboySharp
{
    public class ScreenRenderer : IDisposable
    {
        private readonly GL _gl;
        private uint _vao;
        private uint _vbo;
        private uint _ebo;
        private uint _shaderProgram;
        private uint _texture;
        private int _scanlineUniformLocation = -1;

        /// <summary>When true, the fragment shader applies the LCD scanline darkening effect.</summary>
        public bool Scanlines { get; set; } = false;

        // How strongly the scanline effect darkens alternate rows when enabled.
        private const float ScanlineStrength = 0.35f;

        private static readonly float[] _vertices =
        {
            // Position      Texture Coords
             1.0f,  1.0f,    1.0f, 0.0f, // Top Right
             1.0f, -1.0f,    1.0f, 1.0f, // Bottom Right
            -1.0f, -1.0f,    0.0f, 1.0f, // Bottom Left
            -1.0f,  1.0f,    0.0f, 0.0f  // Top Left
        };

        private static readonly uint[] _indices = { 0, 1, 3, 1, 2, 3 };

        public ScreenRenderer(GL gl)
        {
            _gl = gl;
        }

        public unsafe void Load()
        {
            // --- Buffer Setup ---
            _vao = _gl.GenVertexArray();
            _gl.BindVertexArray(_vao);

            _vbo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            fixed (void* p = _vertices)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (uint)(_vertices.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);
            }
            _ebo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
            fixed (void* p = _indices)
            {
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (uint)(_indices.Length * sizeof(uint)), p, BufferUsageARB.StaticDraw);
            }
            // --- Shader Setup ---
            // Load the shaders from the "shaders" folder next to the executable (AppContext.BaseDirectory)
            // rather than the current working directory. This matters because a launched app doesn't
            // necessarily run from its own folder — most importantly, a macOS .app double-clicked in
            // Finder runs with the working directory set to "/", which would break a relative path.
            string shaderDir = Path.Combine(AppContext.BaseDirectory, "shaders");
            var vertexSource = File.ReadAllText(Path.Combine(shaderDir, "screen.vert"));
            var fragmentSource = File.ReadAllText(Path.Combine(shaderDir, "screen.frag"));
            _shaderProgram = CreateShaderProgram(vertexSource, fragmentSource);

            // Cache the scanline uniform location so Render can toggle the effect cheaply.
            _scanlineUniformLocation = _gl.GetUniformLocation(_shaderProgram, "uScanlineStrength");

            // --- Vertex Attribute Pointers ---
            // Position attribute
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), null);
            _gl.EnableVertexAttribArray(0);
            // Texture coordinate attribute
            _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);

            // --- Texture Setup ---
            _texture = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _texture);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

            // Allocate texture memory on the GPU
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, 
                (uint)GameboyConstants.ScreenWidth, (uint)GameboyConstants.ScreenHeight, 
                0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
        }

        public unsafe void Render(uint[] frameBuffer)
        {
            // The PPU stores each pixel as a uint in AABBGGRR order. On a little-endian
            // target (all our platforms: x64/arm64) that uint is laid out in memory as the
            // bytes [R, G, B, A], which is exactly what GL_RGBA + GL_UNSIGNED_BYTE expects.
            // So we can upload the framebuffer straight from its pinned pointer — no per-frame
            // byte[] allocation and no per-pixel conversion loop (those were ~92 KB of Gen0
            // garbage every frame on the same thread that services audio).
            _gl.BindTexture(TextureTarget.Texture2D, _texture);
            fixed (uint* ptr = frameBuffer)
            {
                _gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0,
                    (uint)GameboyConstants.ScreenWidth, (uint)GameboyConstants.ScreenHeight,
                    PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
            }

            // Draw the textured quad
            _gl.UseProgram(_shaderProgram);
            if (_scanlineUniformLocation >= 0)
            {
                _gl.Uniform1(_scanlineUniformLocation, Scanlines ? ScanlineStrength : 0.0f);
            }
            _gl.BindVertexArray(_vao);
            _gl.DrawElements(PrimitiveType.Triangles, (uint)_indices.Length, DrawElementsType.UnsignedInt, null);
        }

        private uint CreateShaderProgram(string vertexSource, string fragmentSource)
        {
            uint vertexShader = CompileShader(ShaderType.VertexShader, vertexSource);
            uint fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentSource);

            uint program = _gl.CreateProgram();
            _gl.AttachShader(program, vertexShader);
            _gl.AttachShader(program, fragmentShader);
            _gl.LinkProgram(program);

            _gl.GetProgram(program, GLEnum.LinkStatus, out var status);
            if (status == 0)
            {
                throw new Exception($"Error linking shader program: {_gl.GetProgramInfoLog(program)}");
            }

            _gl.DeleteShader(vertexShader);
            _gl.DeleteShader(fragmentShader);
            return program;
        }

        private uint CompileShader(ShaderType type, string source)
        {
            uint shader = _gl.CreateShader(type);
            _gl.ShaderSource(shader, source);
            _gl.CompileShader(shader);

            string infoLog = _gl.GetShaderInfoLog(shader);
            if (!string.IsNullOrWhiteSpace(infoLog))
            {
                throw new Exception($"Error compiling {type}: {infoLog}");
            }
            return shader;
        }

        public void Dispose()
        {
            _gl.DeleteVertexArray(_vao);
            _gl.DeleteBuffer(_vbo);
            _gl.DeleteBuffer(_ebo);
            _gl.DeleteProgram(_shaderProgram);
            _gl.DeleteTexture(_texture);
        }
    }
}