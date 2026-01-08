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
            // Assumes shaders are in a "shaders" folder and set to "Copy if newer"
            var vertexSource = File.ReadAllText("shaders/screen.vert");
            var fragmentSource = File.ReadAllText("shaders/screen.frag");
            _shaderProgram = CreateShaderProgram(vertexSource, fragmentSource);

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
            // Convert uint[] (AABBGGRR) to byte[] (RGBA) for OpenGL
            var byteBuffer = new byte[frameBuffer.Length * 4];
            for (int i = 0; i < frameBuffer.Length; i++)
            {
                uint color = frameBuffer[i];
                int byteIndex = i * 4;
                byteBuffer[byteIndex]     = (byte)(color & 0xFF);         // R
                byteBuffer[byteIndex + 1] = (byte)((color >> 8) & 0xFF);  // G
                byteBuffer[byteIndex + 2] = (byte)((color >> 16) & 0xFF); // B
                byteBuffer[byteIndex + 3] = (byte)((color >> 24) & 0xFF); // A
            }

            // Update texture on the GPU
            _gl.BindTexture(TextureTarget.Texture2D, _texture);
            fixed (byte* ptr = byteBuffer)
            {
                _gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0,
                    (uint)GameboyConstants.ScreenWidth, (uint)GameboyConstants.ScreenHeight,
                    PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
            }

            // Draw the textured quad
            _gl.UseProgram(_shaderProgram);
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