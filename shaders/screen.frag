#version 330 core
out vec4 FragColor;
in vec2 TexCoord;
uniform sampler2D ourTexture;

// Strength of the LCD scanline effect, 0.0 = off. Set from ScreenRenderer based on the user's
// Video setting. We darken every other Game Boy scanline to evoke the original LCD grid.
uniform float uScanlineStrength;

void main()
{
    vec4 color = texture(ourTexture, TexCoord);

    if (uScanlineStrength > 0.0)
    {
        // 144 physical scanlines: darken the odd ones to suggest the LCD's row gaps.
        float line = floor(TexCoord.y * 144.0);
        if (mod(line, 2.0) >= 1.0)
        {
            color.rgb *= (1.0 - uScanlineStrength);
        }
    }

    FragColor = color;
}
