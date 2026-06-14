using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using X4StationBuilder.Core.Services;

namespace X4StationBuilder.App.ViewModels;

/// <summary>
/// Editable view model backing the Settings dialog. Edits a copy of the user's preferences (name
/// prefix + default docks) and writes them back into <see cref="AppSettings"/> only when the user
/// saves. Default-dock rows reuse <see cref="DockRequestRow"/> against the catalog's available docks.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    public SettingsViewModel(AppSettings settings, IReadOnlyList<DockOption> availableDockModules)
    {
        AvailableDockModules = availableDockModules;
        _stationNamePrefix = settings.StationNamePrefix ?? string.Empty;

        foreach (var entry in settings.DefaultDocks)
        {
            if (string.IsNullOrWhiteSpace(entry.Macro))
            {
                continue;
            }

            var option = availableDockModules.FirstOrDefault(
                m => string.Equals(m.Module.Macro, entry.Macro, StringComparison.OrdinalIgnoreCase));
            if (option is not null)
            {
                DefaultDocks.Add(new DockRequestRow(availableDockModules, option, Math.Max(0, entry.Count)));
            }
        }
    }

    /// <summary>Dock/pier options the user can choose from (sorted, from the catalog).</summary>
    public IReadOnlyList<DockOption> AvailableDockModules { get; }

    /// <summary>The editable default docking set.</summary>
    public ObservableCollection<DockRequestRow> DefaultDocks { get; } = [];

    [ObservableProperty]
    private string _stationNamePrefix;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveDockCommand))]
    private DockRequestRow? _selectedDock;

    private bool CanAddDock => AvailableDockModules.Count > 0;

    [RelayCommand(CanExecute = nameof(CanAddDock))]
    private void AddDock()
    {
        var row = new DockRequestRow(AvailableDockModules, AvailableDockModules.FirstOrDefault());
        DefaultDocks.Add(row);
        SelectedDock = row;
    }

    private bool CanRemoveDock => SelectedDock is not null;

    [RelayCommand(CanExecute = nameof(CanRemoveDock))]
    private void RemoveDock(DockRequestRow? row)
    {
        row ??= SelectedDock;
        if (row is not null)
        {
            DefaultDocks.Remove(row);
        }
    }

    /// <summary>Writes the edited values back into <paramref name="settings"/>.</summary>
    public void ApplyTo(AppSettings settings)
    {
        var prefix = StationNamePrefix?.Trim();
        settings.StationNamePrefix = string.IsNullOrEmpty(prefix) ? null : prefix;

        settings.DefaultDocks = DefaultDocks
            .Where(d => d.SelectedModule is not null
                && !string.IsNullOrWhiteSpace(d.SelectedModule.Module.Macro)
                && d.Count > 0)
            .Select(d => new DockDefault { Macro = d.SelectedModule!.Module.Macro!, Count = d.Count })
            .ToList();
    }
}
