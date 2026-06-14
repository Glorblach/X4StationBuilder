using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using X4StationBuilder.App.ViewModels;

namespace X4StationBuilder.App.Views;

/// <summary>
/// Interaction logic for PreviewWindow.xaml. Renders the laid-out station as colour-coded boxes in a
/// <see cref="HelixViewport3D"/>. Helix visuals can't be data-bound directly, so the module boxes and
/// labels are (re)built in code-behind whenever the view model raises
/// <see cref="PreviewViewModel.VisualsChanged"/>. All generated visuals live under a single container
/// so the static scene (lights, ground grid) is left untouched on rebuild.
/// </summary>
public partial class PreviewWindow : Window
{
    private readonly ModelVisual3D _generatedRoot = new();
    private PreviewViewModel? _viewModel;

    public PreviewWindow(PreviewViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        Viewport.Children.Add(_generatedRoot);

        _viewModel.VisualsChanged += OnVisualsChanged;
        Loaded += (_, _) => RebuildVisuals();
        Closed += (_, _) =>
        {
            if (_viewModel is not null)
            {
                _viewModel.VisualsChanged -= OnVisualsChanged;
                _viewModel = null;
            }
        };
    }

    private void OnVisualsChanged(object? sender, EventArgs e) => RebuildVisuals();

    private void RebuildVisuals()
    {
        if (_viewModel is null)
        {
            return;
        }

        _generatedRoot.Children.Clear();

        foreach (var module in _viewModel.Modules)
        {
            _generatedRoot.Children.Add(new BoxVisual3D
            {
                Center = module.Center,
                Length = module.Size.X,
                Width = module.Size.Z,
                Height = module.Size.Y,
                Fill = new SolidColorBrush(module.Color),
            });

            if (_viewModel.ShowLabels && !string.IsNullOrWhiteSpace(module.Label))
            {
                _generatedRoot.Children.Add(new BillboardTextVisual3D
                {
                    Text = module.Label,
                    FontSize = 11,
                    Foreground = Brushes.White,
                    Position = new Point3D(
                        module.Center.X,
                        module.Center.Y + (module.Size.Y / 2) + 0.5,
                        module.Center.Z),
                });
            }
        }

        if (_generatedRoot.Children.Count > 0)
        {
            Viewport.ZoomExtents(0);
        }
    }
}
