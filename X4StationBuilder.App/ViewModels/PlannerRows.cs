using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using X4StationBuilder.Core.Models;

namespace X4StationBuilder.App.ViewModels;

/// <summary>Maps ware categories to a flat badge colour for the picker. Unknown → gray.</summary>
public static class CategoryBadge
{
    private static readonly Dictionary<string, Color> Colors =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Energy"] = Color.FromRgb(0xF2, 0xC0, 0x37),
            ["RawResource"] = Color.FromRgb(0x8D, 0x6E, 0x63),
            ["RefinedResource"] = Color.FromRgb(0xA1, 0x88, 0x7F),
            ["Intermediate"] = Color.FromRgb(0x42, 0xA5, 0xF5),
            ["Intermediate_Tech"] = Color.FromRgb(0x5C, 0x6B, 0xC0),
            ["TechProduct"] = Color.FromRgb(0x7E, 0x57, 0xC2),
            ["FoodIntermediate"] = Color.FromRgb(0x66, 0xBB, 0x6A),
            ["FoodProduct"] = Color.FromRgb(0x43, 0xA0, 0x47),
            ["Medical"] = Color.FromRgb(0xEF, 0x53, 0x50),
        };

    private static readonly Color Default = Color.FromRgb(0x9E, 0x9E, 0x9E);

    /// <summary>A solid, frozen brush for the given category (gray when unknown/null).</summary>
    public static Brush BrushFor(string? category)
    {
        var color = category is not null && Colors.TryGetValue(category, out var c) ? c : Default;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// Maps X4 faction/race tokens and full names to display names, and prefixes module labels with a
/// faction when not already present. Used to disambiguate same-named modules (e.g. several
/// "L Ship Fabrication Bay" variants) and to label faction-specific production modules.
/// </summary>
public static class FactionLabels
{
    private static readonly Dictionary<string, string> Display =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["arg"] = "Argon", ["argon"] = "Argon",
            ["par"] = "Paranid", ["paranid"] = "Paranid",
            ["tel"] = "Teladi", ["teladi"] = "Teladi",
            ["spl"] = "Split", ["split"] = "Split",
            ["ter"] = "Terran", ["terran"] = "Terran", ["atf"] = "Terran",
            ["bor"] = "Boron", ["boron"] = "Boron",
            ["pir"] = "Pirate", ["pirate"] = "Pirate",
            ["xen"] = "Xenon", ["xenon"] = "Xenon",
            ["khk"] = "Kha'ak", ["ktk"] = "Kha'ak", ["khaak"] = "Kha'ak",
        };

    /// <summary>Display name for a faction token/name, or null when unknown/generic.</summary>
    public static string? ToDisplay(string? token) =>
        token is not null && Display.TryGetValue(token.Trim(), out var name) ? name : null;

    /// <summary>
    /// Derives a faction display name for a module, preferring a token parsed from its macro/id
    /// (most reliable for same-named variants), then its factions/races. Null when none resolve.
    /// </summary>
    public static string? ForModule(StationModule module)
    {
        foreach (var token in Tokens(module.Macro).Concat(Tokens(module.Id)))
        {
            if (Display.TryGetValue(token, out var fromMacro))
            {
                return fromMacro;
            }
        }

        return ToDisplay(module.Factions.FirstOrDefault())
               ?? ToDisplay(module.Races.FirstOrDefault());
    }

    /// <summary>
    /// Prefixes <paramref name="name"/> with <paramref name="factionDisplay"/> unless the faction is
    /// null/blank or the name already begins with it (case-insensitive).
    /// </summary>
    public static string Prefix(string name, string? factionDisplay)
    {
        if (string.IsNullOrWhiteSpace(factionDisplay)
            || name.StartsWith(factionDisplay, StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        return $"{factionDisplay} {name}";
    }

    private static IEnumerable<string> Tokens(string? value) =>
        string.IsNullOrEmpty(value)
            ? []
            : value.Split('_', StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>Discriminates the two kinds of entry shown in the picker list.</summary>
public enum PickerKind
{
    /// <summary>A producible ware backed by a dedicated production module.</summary>
    Ware,

    /// <summary>A build module (wharf/shipyard) placed directly on the station.</summary>
    BuildModule,
}

/// <summary>
/// An entry in the picker list: either a producible <see cref="PickerKind.Ware"/> (with a category
/// badge) or a <see cref="PickerKind.BuildModule"/> that wraps a placeable <see cref="StationModule"/>.
/// </summary>
public sealed class WarePickerItem
{
    /// <summary>Underlying ware name (the lookup key for producing a ware); also set for build modules.</summary>
    public required string WareName { get; init; }

    /// <summary>Label shown in the picker list and matched by search. Defaults to <see cref="WareName"/>.</summary>
    public string? DisplayLabel { get; init; }

    /// <summary>The label actually rendered/searched: <see cref="DisplayLabel"/> when set, else <see cref="WareName"/>.</summary>
    public string DisplayName => string.IsNullOrEmpty(DisplayLabel) ? WareName : DisplayLabel!;

    /// <summary>Recipe faction for a ware entry (e.g. "Common", "Terran"); null for build modules.</summary>
    public string? Faction { get; init; }

    public string? Category { get; init; }

    /// <summary>Whether this entry is a ware or a build module.</summary>
    public PickerKind Kind { get; init; } = PickerKind.Ware;

    /// <summary>The build module to place; non-null only when <see cref="Kind"/> is BuildModule.</summary>
    public StationModule? BuildModule { get; init; }

    /// <summary>The specific production module for a ware entry (e.g. the Terran variant); may be null.</summary>
    public StationModule? ProductionModule { get; init; }

    /// <summary>
    /// Group header shown in the picker. For wares this is the resolved category group
    /// (e.g. "Refined Goods"); build modules use "Build Module".
    /// </summary>
    public string GroupLabel { get; init; } = "Wares";

    /// <summary>Sort order for the group header (lower first); build modules sort after wares.</summary>
    public int GroupOrder { get; init; }

    public Brush BadgeBrush => Kind == PickerKind.BuildModule
        ? BuildModuleBadge
        : CategoryBadge.BrushFor(Category);

    private static readonly Brush BuildModuleBadge = CreateBuildModuleBadge();

    private static Brush CreateBuildModuleBadge()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0xB0, 0x8D, 0x57));
        brush.Freeze();
        return brush;
    }
}

/// <summary>A single raw (non-producible) resource consumed by the plan.</summary>
public sealed record RawResourceRow(string WareName, double ItemsPerHour);

/// <summary>
/// A selectable dock/pier option for the dock picker: wraps a <see cref="StationModule"/> and a
/// disambiguating <see cref="Label"/>. Several dock macros share an identical localized name, so the
/// label appends the size and macro to keep each entry distinct.
/// </summary>
public sealed class DockOption
{
    public DockOption(StationModule module)
    {
        Module = module;
    }

    public StationModule Module { get; }

    /// <summary>e.g. "Argon 3-Dock T Pier (M · pier_arg_t_01)"; the macro part is omitted if unknown.</summary>
    public string Label =>
        string.IsNullOrWhiteSpace(Module.Macro)
            ? $"{Module.DisplayName} ({Module.Size})"
            : $"{Module.DisplayName} ({Module.Size} · {Module.Macro})";
}

/// <summary>
/// An editable docking selection: which dock/pier option to include and how many. Mirrors the
/// <see cref="DesiredWareRow"/>/<c>DesiredWare</c> split — this observable row is the UI surface,
/// while <c>DockRequest</c> is the immutable value fed to layout/export. Negative counts are clamped
/// to zero on edit.
/// </summary>
public sealed partial class DockRequestRow : ObservableObject
{
    public DockRequestRow(IReadOnlyList<DockOption> availableModules, DockOption? selected = null, int count = 1)
    {
        AvailableModules = availableModules;
        _selectedModule = selected ?? availableModules.FirstOrDefault();
        _count = count;
    }

    /// <summary>Raised whenever a value changes that affects the assembled module list.</summary>
    public Action? Changed { get; set; }

    /// <summary>Dock/pier options the user can choose from.</summary>
    public IReadOnlyList<DockOption> AvailableModules { get; }

    [ObservableProperty]
    private DockOption? _selectedModule;

    [ObservableProperty]
    private int _count;

    partial void OnSelectedModuleChanged(DockOption? value) => Changed?.Invoke();

    partial void OnCountChanged(int value)
    {
        if (value < 0)
        {
            Count = 0; // re-enters this handler with 0, then invokes Changed
            return;
        }

        Changed?.Invoke();
    }
}

/// <summary>
/// An editable storage selection: which storage module to include and how many. Mirrors
/// <see cref="DockRequestRow"/> — this observable row is the UI surface, while the assembled
/// <c>LayoutItem</c> is fed to layout/export. Rows seed from <see cref="Services.StoragePlanner"/>
/// but the user can override them. The <see cref="Changed"/> callback receives <c>true</c> when the
/// change came from a user edit (so the view-model can stop auto-refreshing) and <c>false</c> when the
/// row is being populated programmatically.
/// </summary>
public sealed partial class StorageRow : ObservableObject
{
    private bool _suppress;

    public StorageRow(IReadOnlyList<DockOption> availableModules, DockOption? selected = null, int count = 1)
    {
        AvailableModules = availableModules;
        _selectedModule = selected ?? availableModules.FirstOrDefault();
        _count = count;
    }

    /// <summary>Raised when a value changes; the argument is true for user edits, false for seeding.</summary>
    public Action<bool>? Changed { get; set; }

    /// <summary>Storage module options the user can choose from.</summary>
    public IReadOnlyList<DockOption> AvailableModules { get; }

    [ObservableProperty]
    private DockOption? _selectedModule;

    [ObservableProperty]
    private int _count;

    /// <summary>Sets the module/count without raising a user-edit notification (used when seeding).</summary>
    public void SetSilently(DockOption? module, int count)
    {
        _suppress = true;
        SelectedModule = module;
        Count = count;
        _suppress = false;
    }

    partial void OnSelectedModuleChanged(DockOption? value) => Changed?.Invoke(!_suppress);

    partial void OnCountChanged(int value)
    {
        if (value < 0)
        {
            Count = 0; // re-enters this handler with 0, then notifies
            return;
        }

        Changed?.Invoke(!_suppress);
    }
}

/// <summary>
/// An editable build-module (wharf/shipyard) selection: which build module to place and how many.
/// Mirrors <see cref="DockRequestRow"/> — placement only, with no production chain. Negative counts
/// are clamped to zero on edit.
/// </summary>
public sealed partial class BuildModuleRow : ObservableObject
{
    public BuildModuleRow(StationModule module, int count = 1)
    {
        Module = module;
        _count = count;
    }

    /// <summary>Raised whenever a value changes that affects the assembled module list.</summary>
    public Action? Changed { get; set; }

    /// <summary>The build module to place.</summary>
    public StationModule Module { get; }

    /// <summary>Friendly label for the module (its name when known, else its id).</summary>
    public string ModuleName => Module.DisplayName;

    [ObservableProperty]
    private int _count;

    partial void OnCountChanged(int value)
    {
        if (value < 0)
        {
            Count = 0; // re-enters this handler with 0, then invokes Changed
            return;
        }

        Changed?.Invoke();
    }
}

/// <summary>A resolved production requirement, labelled by its station module.</summary>
public sealed record RequiredModuleRow(
    string ModuleName,
    string Faction,
    int Modules,
    double ItemsPerHour,
    int Workers);

/// <summary>
/// An editable desired-ware row. The number of <see cref="Stations"/> (production modules) is the
/// quantity the user sets and is held fixed when workforce is toggled; <see cref="ItemsPerHour"/> is
/// the resulting output, which rises when the modules are staffed (workforce on). The value fed to
/// the calculator is <see cref="ItemsPerHour"/>, so it reproduces exactly <see cref="Stations"/>
/// modules and scales the dependencies to the (boosted) production.
/// </summary>
public sealed partial class DesiredWareRow : ObservableObject
{
    private readonly Ware _ware;
    private bool _suppress;
    private bool _workforceEnabled;

    public DesiredWareRow(
        Ware ware,
        string selectedFaction,
        double stations,
        bool workforceEnabled,
        string? moduleName = null,
        string? preferredModuleId = null)
    {
        _ware = ware;
        _workforceEnabled = workforceEnabled;
        AvailableFactions = ware.ProducibleByFactions.ToList();
        ModuleName = string.IsNullOrWhiteSpace(moduleName) ? $"{ware.Name} Production" : moduleName!;
        PreferredModuleId = preferredModuleId;
        _selectedFaction = selectedFaction;
        _stations = stations;
        _itemsPerHour = stations * EffectiveAmount;
    }

    /// <summary>Raised whenever a value changes that affects the production calculation.</summary>
    public Action? Changed { get; set; }

    public string WareName => _ware.Name;

    /// <summary>The production module that makes this ware (e.g. "Energy Cell Production").</summary>
    public string ModuleName { get; }

    /// <summary>Id of the specific production module to place (e.g. the Terran variant), or null.</summary>
    public string? PreferredModuleId { get; }

    public IReadOnlyList<string> AvailableFactions { get; }

    /// <summary>
    /// Whether the station is staffed. When true and this recipe employs workers, the module output
    /// is boosted by the workforce multiplier, so the same number of modules produces more.
    /// </summary>
    public bool WorkforceEnabled
    {
        get => _workforceEnabled;
        set
        {
            if (_workforceEnabled == value)
            {
                return;
            }

            _workforceEnabled = value;

            // Hold the module count fixed; the output (items/hour) rises/falls with staffing.
            _suppress = true;
            ItemsPerHour = Stations * EffectiveAmount;
            _suppress = false;
            Changed?.Invoke();
        }
    }

    private Recipe? Recipe =>
        _ware.RecipesByFaction.TryGetValue(SelectedFaction, out var recipe) ? recipe : null;

    /// <summary>
    /// Per-module hourly output, boosted by the workforce multiplier when the station is staffed and
    /// this recipe employs workers — mirrors <c>FactoryGroup.EffectiveAmount</c> in the calculator.
    /// </summary>
    private double EffectiveAmount
    {
        get
        {
            var recipe = Recipe;
            if (recipe is null)
            {
                return 0;
            }

            return _workforceEnabled && recipe.WorkforceCapacity > 0
                ? recipe.Amount * recipe.WorkforceMultiplier
                : recipe.Amount;
        }
    }

    [ObservableProperty]
    private string _selectedFaction;

    [ObservableProperty]
    private double _itemsPerHour;

    [ObservableProperty]
    private double _stations;

    partial void OnSelectedFactionChanged(string value)
    {
        // Keep the module count fixed; recompute the output against the new recipe.
        _suppress = true;
        ItemsPerHour = Stations * EffectiveAmount;
        _suppress = false;
        Changed?.Invoke();
    }

    partial void OnItemsPerHourChanged(double value)
    {
        if (_suppress)
        {
            return;
        }

        _suppress = true;
        Stations = EffectiveAmount > 0 ? value / EffectiveAmount : 0;
        _suppress = false;
        Changed?.Invoke();
    }

    partial void OnStationsChanged(double value)
    {
        if (_suppress)
        {
            return;
        }

        _suppress = true;
        ItemsPerHour = value * EffectiveAmount;
        _suppress = false;
        Changed?.Invoke();
    }
}
