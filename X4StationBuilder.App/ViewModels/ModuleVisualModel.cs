using System.Windows.Media;
using System.Windows.Media.Media3D;
using X4StationBuilder.Core.Models;

namespace X4StationBuilder.App.ViewModels;

/// <summary>
/// A single module rendered in the 3D preview: its centre and size (in display metres), a
/// type-based colour, and a short label. Built from a <see cref="PlacedModule"/> produced by the
/// <see cref="Core.Services.StationLayoutEngine"/>.
/// </summary>
/// <remarks>
/// Blueprint positions/footprints are in centimetres; the preview divides by
/// <see cref="BlueprintToMetres"/> so one standard 600 cm module spans 6 display metres. X4's Y is
/// up, matching Helix's default <c>UpDirection</c>, so axes map directly (X→X, Y→Y, Z→Z).
/// </remarks>
public sealed class ModuleVisualModel
{
    /// <summary>Blueprint centimetres per display metre.</summary>
    public const double BlueprintToMetres = 100.0;

    /// <summary>Module centre in display metres.</summary>
    public Point3D Center { get; init; }

    /// <summary>Full module size (each axis) in display metres.</summary>
    public Vector3D Size { get; init; }

    /// <summary>Fill colour, derived from the module kind.</summary>
    public Color Color { get; init; }

    /// <summary>Short label (module display name) shown as a billboard.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Builds a visual model from a positioned module.</summary>
    public static ModuleVisualModel FromPlaced(PlacedModule placed)
    {
        ArgumentNullException.ThrowIfNull(placed);

        var footprint = FootprintSize(placed.Module);
        return new ModuleVisualModel
        {
            Center = new Point3D(
                placed.Position.X / BlueprintToMetres,
                placed.Position.Y / BlueprintToMetres,
                placed.Position.Z / BlueprintToMetres),
            Size = new Vector3D(
                Math.Max(footprint.X, 0.1) / BlueprintToMetres,
                Math.Max(footprint.Y, 0.1) / BlueprintToMetres,
                Math.Max(footprint.Z, 0.1) / BlueprintToMetres),
            Color = ColorFor(placed.Module.Kind),
            Label = placed.Module.DisplayName,
        };
    }

    /// <summary>
    /// Full footprint size (cm) for a module: from parsed geometry when present, else a
    /// per-<see cref="ModuleSize"/> placeholder mirroring the layout engine's fallback.
    /// </summary>
    private static Vec3 FootprintSize(StationModule module)
    {
        var size = module.Geometry?.Size ?? Vec3.Zero;
        if (size.X > 0 || size.Y > 0 || size.Z > 0)
        {
            // Substitute a placeholder on any axis that geometry didn't cover (e.g. modules that
            // only snap on one axis read 0 on the others).
            var fb = PlaceholderHalfExtent(module.Size) * 2;
            return new Vec3(
                size.X > 0 ? size.X : fb,
                size.Y > 0 ? size.Y : fb,
                size.Z > 0 ? size.Z : fb);
        }

        var full = PlaceholderHalfExtent(module.Size) * 2;
        return new Vec3(full, full, full);
    }

    private static double PlaceholderHalfExtent(ModuleSize size) => size switch
    {
        ModuleSize.S => 300.0,
        ModuleSize.M => 600.0,
        ModuleSize.L => 900.0,
        ModuleSize.XL => 1200.0,
        _ => 300.0,
    };

    /// <summary>Colour-codes a module by its broad kind.</summary>
    public static Color ColorFor(ModuleKind kind) => kind switch
    {
        ModuleKind.Production => Color.FromRgb(0x42, 0x85, 0xF4), // blue
        ModuleKind.Storage => Color.FromRgb(0xF4, 0xB4, 0x00),    // gold/yellow
        ModuleKind.Dock => Color.FromRgb(0xE5, 0x39, 0x35),       // red
        ModuleKind.Pier => Color.FromRgb(0xE5, 0x39, 0x35),       // red
        ModuleKind.Habitat => Color.FromRgb(0x43, 0xA0, 0x47),    // green
        ModuleKind.Connection => Color.FromRgb(0x9E, 0x9E, 0x9E), // gray
        _ => Color.FromRgb(0xBD, 0xBD, 0xBD),                     // light gray
    };
}
