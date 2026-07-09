using SpatialAI.Core.Model;

namespace SpatialAI.Core.Furniture;

/// <summary>
/// Color helpers for the low-poly, per-part furniture look. A piece takes one primary color and
/// derives material accents (wood legs, metal frame, dark screen, ...) from a small fixed palette.
/// </summary>
public static class Palette
{
    public static readonly Rgba Wood     = new(0.55f, 0.36f, 0.20f);
    public static readonly Rgba WoodDark = new(0.34f, 0.22f, 0.12f);
    public static readonly Rgba Metal    = new(0.66f, 0.68f, 0.71f);
    public static readonly Rgba MetalDark= new(0.28f, 0.29f, 0.32f);
    public static readonly Rgba Foliage  = new(0.30f, 0.55f, 0.28f);
    public static readonly Rgba FoliageDark = new(0.20f, 0.42f, 0.20f);
    public static readonly Rgba Clay     = new(0.70f, 0.40f, 0.30f);
    public static readonly Rgba Screen   = new(0.06f, 0.07f, 0.09f);
    public static readonly Rgba White    = new(0.92f, 0.92f, 0.90f);
    public static readonly Rgba Ceramic  = new(0.95f, 0.95f, 0.96f);
    public static readonly Rgba Steel    = new(0.74f, 0.76f, 0.79f);
    public static readonly Rgba SteelDark= new(0.40f, 0.42f, 0.45f);
    public static readonly Rgba Stone    = new(0.55f, 0.55f, 0.58f);
    public static readonly Rgba SafetyYellow = new(0.92f, 0.74f, 0.10f);
    public static readonly Rgba SafetyOrange = new(0.90f, 0.42f, 0.10f);
    public static readonly Rgba Concrete = new(0.62f, 0.62f, 0.60f);

    public static Rgba Darken(Rgba c, float amt) =>
        new(c.R * (1 - amt), c.G * (1 - amt), c.B * (1 - amt), c.A);

    public static Rgba Lighten(Rgba c, float amt) =>
        new(c.R + (1 - c.R) * amt, c.G + (1 - c.G) * amt, c.B + (1 - c.B) * amt, c.A);

    public static Rgba Mix(Rgba a, Rgba b, float t) =>
        new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, 1f);
}
