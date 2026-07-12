namespace NFMWorldLibrary;

public enum OutlineDistanceMode
{
    Fixed = 0,
    DynamicLineSize = 1,
    NfmStyleCull = 2
}

public static partial class World
{
    public static OutlineDistanceMode LineDistanceMode = OutlineDistanceMode.NfmStyleCull;
    public static float OutlineCullDistance = 3000f;
    public static float OutlineDynamicMinimumScale = 0.25f;
}
