using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using X4StationBuilder.Core.Models;

namespace X4StationBuilder.App.ViewModels;

/// <summary>
/// View model for the 3D station preview window. Holds the colour-coded module visuals built from a
/// <see cref="LayoutResult"/> and a rebuild callback so the user can refresh the scene against the
/// current planner state.
/// </summary>
public partial class PreviewViewModel : ObservableObject
{
    private readonly Func<LayoutResult?> _buildLayout;

    /// <param name="buildLayout">
    /// Rebuilds the layout from the current planner state. Returns null when nothing can be laid out.
    /// </param>
    public PreviewViewModel(Func<LayoutResult?> buildLayout)
    {
        _buildLayout = buildLayout ?? throw new ArgumentNullException(nameof(buildLayout));
        Refresh();
    }

    /// <summary>Module box visuals rebuilt whenever the layout changes.</summary>
    public ObservableCollection<ModuleVisualModel> Modules { get; } = [];

    /// <summary>Raised after <see cref="Modules"/> has been rebuilt so the view can redraw.</summary>
    public event EventHandler? VisualsChanged;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private bool _showLabels = true;

    /// <summary>Rebuilds the scene from the current planner state.</summary>
    [RelayCommand]
    public void Refresh()
    {
        LayoutResult? layout;
        try
        {
            layout = _buildLayout();
        }
        catch (Exception ex)
        {
            Modules.Clear();
            Summary = $"Layout failed: {ex.Message}";
            VisualsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        BuildFrom(layout);
    }

    /// <summary>Populates <see cref="Modules"/> from a layout result (null/empty clears the scene).</summary>
    public void BuildFrom(LayoutResult? layout)
    {
        Modules.Clear();

        if (layout is null || layout.Modules.Count == 0)
        {
            Summary = "Nothing to preview yet — add a ware or dock, then refresh.";
            VisualsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        foreach (var placed in layout.Modules)
        {
            Modules.Add(ModuleVisualModel.FromPlaced(placed));
        }

        var size = layout.BoundingBoxSize;
        Summary =
            $"{layout.Modules.Count} modules — bounding box " +
            $"{size.X / ModuleVisualModel.BlueprintToMetres:N0} × " +
            $"{size.Y / ModuleVisualModel.BlueprintToMetres:N0} × " +
            $"{size.Z / ModuleVisualModel.BlueprintToMetres:N0} m";

        VisualsChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnShowLabelsChanged(bool value) => VisualsChanged?.Invoke(this, EventArgs.Empty);
}
