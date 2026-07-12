using Microsoft.Xna.Framework.Graphics;

namespace NFMWorld;

public struct InstanceData(
    Matrix world,
    bool getsShadowed = false,
    float alphaOverride = 1.0f,
    bool isFullbright = false,
    bool glow = false,
    float lineThicknessScale = 1.0f
)
{
    public static VertexDeclaration InstanceDeclaration { get; } = new VertexDeclaration
    (
        new VertexElement(0,  VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 3),
        new VertexElement(16, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 4),
        new VertexElement(32, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 5),
        new VertexElement(48, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 6),
        new VertexElement(64, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 7),
        new VertexElement(80, VertexElementFormat.Single, VertexElementUsage.TextureCoordinate, 8)
    );
    
    public Matrix World = Matrix.Transpose(world);
    public Vector4 AdditionalData = new(getsShadowed ? 1.0f : 0.0f, alphaOverride, isFullbright ? 1.0f : 0.0f, glow ? 1.0f : 0.0f); // x: GetsShadowed (1.0 or 0.0), y: AlphaOverride, z: IsFullbright (1.0 or 0.0), w: Glow (1.0 or 0.0)
    public float LineThicknessScale = lineThicknessScale;
}
