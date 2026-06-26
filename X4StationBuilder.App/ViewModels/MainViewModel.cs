using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using X4StationBuilder.Core.Data;
using X4StationBuilder.Core.Models;
using X4StationBuilder.Core.Services;

namespace X4StationBuilder.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const string WorkforceWareName = "Workforce";
    private static readonly string[] FallbackWorkforceFactions = ["Argon", "Paranid", "Teladi"];

    /// <summary>Picker group sort order for build modules: after all ware category groups.</summary>
    private const int BuildModuleGroupOrder = 900;

    /// <summary>Factions whose modules the player cannot build (excluded from the picker).</summary>
    private static bool IsNonPlayerFaction(string? factionDisplay) =>
        factionDisplay is "Xenon" or "Kha'ak";

    /// <summary>Preferred display order for the workforce-species dropdown.</summary>
    private static readonly string[] WorkforceFactionOrder =
        ["Argon", "Paranid", "Teladi", "Split", "Terran", "Boron"];

    /// <summary>
    /// The workforce-species options: factions that have usable (worker-housing) habitat modules in
    /// the catalog, unioned with any "Workforce" pseudo-recipe factions (bundled data), ordered by
    /// <see cref="WorkforceFactionOrder"/>. Falls back to the built-in list when none resolve.
    /// </summary>
    private IReadOnlyList<string> GetWorkforceFactionOptions()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var habitat in _modules.GetHabitatModules().Where(m => m.WorkforceCapacity > 0))
        {
            var faction = FactionLabels.ForModule(habitat);
            if (faction is not null && !IsNonPlayerFaction(faction))
            {
                set.Add(faction);
            }
        }

        // Bundled data carries a "Workforce" pseudo-recipe whose factions also drive food demand.
        if (_wares.GetByName(WorkforceWareName) is { ProducibleByFactions.Count: > 0 } workforce)
        {
            foreach (var faction in workforce.ProducibleByFactions)
            {
                set.Add(faction);
            }
        }

        if (set.Count == 0)
        {
            return FallbackWorkforceFactions;
        }

        return set
            .OrderBy(f =>
            {
                var i = Array.FindIndex(WorkforceFactionOrder, x => string.Equals(x, f, StringComparison.OrdinalIgnoreCase));
                return i < 0 ? int.MaxValue : i;
            })
            .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Chooses the recipe faction key to use for a ware when a specific production module is picked:
    /// the module's owner faction when the ware has a matching recipe, else the ware's default/first.
    /// (Production modules and recipe methods are separate axes — e.g. energy cells have one "Common"
    /// recipe but several modules.)
    /// </summary>
    private static string ResolveRecipeFaction(Ware ware, StationModule module)
    {
        var owner = FactionLabels.ForModule(module);
        if (owner is not null)
        {
            var match = ware.ProducibleByFactions
                .FirstOrDefault(f => string.Equals(f, owner, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        if (ware.DefaultFaction is not null && ware.RecipesByFaction.ContainsKey(ware.DefaultFaction))
        {
            return ware.DefaultFaction;
        }

        return ware.ProducibleByFactions[0];
    }

    private readonly SettingsStore _settingsStore;
    private readonly ScannedDataStore _dataStore;
    private readonly GameFolderScanner _scanner;
    private readonly AppSettings _settings;

    private WareRepository _wares;
    private ModuleCatalog _modules;
    private ProductionCalculator _calculator;
    private readonly WareGroupResolver _groupResolver = new();
    private readonly List<WarePickerItem> _allPickerItems = [];
    private bool _suspendRecompute;
    private ProductionResult? _lastResult;

    public MainViewModel()
        : this(new SettingsStore(), new ScannedDataStore(), new GameFolderScanner())
    {
    }

    public MainViewModel(SettingsStore settingsStore, ScannedDataStore dataStore, GameFolderScanner scanner)
    {
        _settingsStore = settingsStore;
        _dataStore = dataStore;
        _scanner = scanner;
        _settings = settingsStore.Load();

        _wares = WareRepository.CreatePreferringScanned(dataStore);
        _modules = ModuleCatalog.CreatePreferringScanned(dataStore);
        _calculator = new ProductionCalculator(_wares, _modules);

        DesiredWares.CollectionChanged += (_, _) => Recompute();
        BuildModules.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasBuildModules));

        LoadDataDependentState();

        if (_dataStore.LoadMetadata() is { } metadata)
        {
            _gamePath = metadata.GamePath;
            ApplySummary(metadata);
        }
        else
        {
            _gamePath = _settings.GamePath ?? GameFolderLocator.TryFindDefault();
        }
    }

    /// <summary>(Re)builds the picker list and workforce-faction options from the current data.</summary>
    private void LoadDataDependentState()
    {
        // Rebuilding WorkforceFactions clears the ComboBox's ItemsSource, which makes WPF transiently
        // write null back into WorkforceFaction. Suspend recompute so that null never reaches the
        // calculator; the faction is repaired below before recompute resumes.
        var priorSuspend = _suspendRecompute;
        _suspendRecompute = true;
        try
        {
            _allPickerItems.Clear();
            foreach (var ware in _wares.AllWares.Where(w => w.IsProducible))
            {
                var productionModules = _modules.GetProductionModules(ware);
                if (productionModules.Count == 0)
                {
                    // Wares built in wharfs/shipyards (ships, drones, deployables) have a recipe but
                    // no production module; the wharf/shipyard itself is offered as a Build module.
                    continue;
                }

                var (groupName, groupOrder) = _groupResolver.Resolve(ware);

                // One entry per distinct, player-buildable production module — mirroring the in-game
                // build menu (e.g. "Energy Cell Production" + "Terran Energy Cell Production"). Enemy
                // (Xenon/Kha'ak) modules and duplicate-named variants are excluded.
                var seenLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var module in productionModules)
                {
                    if (IsNonPlayerFaction(FactionLabels.ForModule(module)))
                    {
                        continue;
                    }

                    var label = string.IsNullOrWhiteSpace(module.Name)
                        ? $"{ware.Name} Production"
                        : module.Name!;

                    if (!seenLabels.Add(label))
                    {
                        continue;
                    }

                    _allPickerItems.Add(new WarePickerItem
                    {
                        WareName = ware.Name,
                        DisplayLabel = label,
                        Faction = ResolveRecipeFaction(ware, module),
                        ProductionModule = module,
                        Category = ware.Category,
                        GroupLabel = groupName,
                        GroupOrder = groupOrder,
                    });
                }
            }

            foreach (var module in _modules.Modules.Where(m => m.Kind == ModuleKind.Build))
            {
                _allPickerItems.Add(new WarePickerItem
                {
                    WareName = module.DisplayName,
                    DisplayLabel = FactionLabels.Prefix(module.DisplayName, FactionLabels.ForModule(module)),
                    Kind = PickerKind.BuildModule,
                    BuildModule = module,
                    GroupLabel = "Build Module",
                    GroupOrder = BuildModuleGroupOrder,
                });
            }

            // Order so the CollectionViewSource renders groups in the curated order (category groups
            // first, then Build Module), with entries alphabetised by display label within a group.
            _allPickerItems.Sort((a, b) =>
            {
                var byOrder = a.GroupOrder.CompareTo(b.GroupOrder);
                if (byOrder != 0)
                {
                    return byOrder;
                }

                var byGroup = string.Compare(a.GroupLabel, b.GroupLabel, StringComparison.OrdinalIgnoreCase);
                return byGroup != 0
                    ? byGroup
                    : string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
            });

            ApplyPickerFilter();

            var factions = GetWorkforceFactionOptions();

            WorkforceFactions.Clear();
            foreach (var faction in factions)
            {
                WorkforceFactions.Add(faction);
            }

            if (string.IsNullOrWhiteSpace(WorkforceFaction) || !WorkforceFactions.Contains(WorkforceFaction))
            {
                WorkforceFaction = WorkforceFactions.FirstOrDefault() ?? WorkforceFaction;
            }

            LoadDockOptions();
            LoadStorageOptions();
        }
        finally
        {
            _suspendRecompute = priorSuspend;
        }
    }

    /// <summary>
    /// (Re)builds the available dock/pier list from the catalog. Drops any selected dock rows whose
    /// module is no longer available, then seeds a default dock when none remain.
    /// </summary>
    private void LoadDockOptions()
    {
        AvailableDockModules.Clear();
        var sorted = _modules.GetDockModules().Concat(_modules.GetPierModules())
            .OrderBy(m => DockFactionKey(m), StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Kind == ModuleKind.Pier ? 1 : 0)
            .ThenBy(m => SizeSortKey(m.Size))
            .ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Macro, StringComparer.OrdinalIgnoreCase);

        // Several logical modules can share one placement macro (faction variants). Collapse them to a
        // single option keyed by macro (or id when the macro is unresolved) so the list has no dupes.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dock in sorted)
        {
            var key = string.IsNullOrEmpty(dock.Macro) ? "id:" + dock.Id : "macro:" + dock.Macro;
            if (seen.Add(key))
            {
                AvailableDockModules.Add(new DockOption(dock));
            }
        }

        // Re-point existing rows at the equivalent module in the new catalog (match by macro/id);
        // drop rows whose dock no longer exists.
        var stale = new List<DockRequestRow>();
        foreach (var row in Docks)
        {
            var match = AvailableDockModules.FirstOrDefault(m => SameModule(m, row.SelectedModule));
            if (match is null)
            {
                stale.Add(row);
            }
            else
            {
                row.SelectedModule = match;
            }
        }

        foreach (var row in stale)
        {
            Docks.Remove(row);
        }

        if (Docks.Count == 0 && AvailableDockModules.Count > 0)
        {
            SeedDefaultDocks();
        }
    }

    /// <summary>
    /// Seeds <see cref="Docks"/> from the user's configured <see cref="AppSettings.DefaultDocks"/>,
    /// resolving each stored macro against the current catalog. Entries whose macro isn't available
    /// are skipped. Falls back to a single <see cref="PreferredDefaultDock"/> when nothing is
    /// configured or nothing resolves.
    /// </summary>
    private void SeedDefaultDocks()
    {
        foreach (var entry in _settings.DefaultDocks)
        {
            if (entry.Count <= 0 || string.IsNullOrWhiteSpace(entry.Macro))
            {
                continue;
            }

            var option = AvailableDockModules.FirstOrDefault(
                m => string.Equals(m.Module.Macro, entry.Macro, StringComparison.OrdinalIgnoreCase));
            if (option is not null)
            {
                Docks.Add(NewDockRow(option, entry.Count));
            }
        }

        if (Docks.Count == 0)
        {
            Docks.Add(NewDockRow(PreferredDefaultDock()));
        }
    }

    /// <summary>
    /// Faction grouping key for a dock/pier. Derived from the macro/id token (e.g. <c>arg</c> in
    /// <c>dockarea_arg_m_station_01_macro</c>) so faction variants that share a macro but carry
    /// different <see cref="StationModule.Factions"/> orderings still group contiguously. Falls back
    /// to the first declared faction, then to a sentinel that sorts faction-less modules last.
    /// </summary>
    private static string DockFactionKey(StationModule module)
    {
        var token = SecondToken(module.Macro) ?? SecondToken(module.Id);
        if (!string.IsNullOrEmpty(token))
        {
            return token;
        }

        return module.Factions.Count > 0 ? module.Factions[0] : "\uffff";
    }

    /// <summary>The second underscore-delimited token (the faction code) of a macro/id, or null.</summary>
    private static string? SecondToken(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var parts = value.Split('_');
        return parts.Length >= 2 ? parts[1] : null;
    }

    private static int SizeSortKey(ModuleSize size) => size switch
    {
        ModuleSize.S => 0,
        ModuleSize.M => 1,
        ModuleSize.L => 2,
        ModuleSize.XL => 3,
        _ => 4,
    };

    private static bool SameModule(DockOption a, DockOption? b)
    {
        if (b is null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(a.Module.Macro) && !string.IsNullOrEmpty(b.Module.Macro))
        {
            return string.Equals(a.Module.Macro, b.Module.Macro, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(a.Module.Id, b.Module.Id, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The dock seeded by default: the first M-size dock, else the first available option.</summary>
    private DockOption? PreferredDefaultDock() =>
        AvailableDockModules.FirstOrDefault(m => m.Module.Kind == ModuleKind.Dock && m.Module.Size == ModuleSize.M)
        ?? AvailableDockModules.FirstOrDefault(m => m.Module.Kind == ModuleKind.Dock)
        ?? AvailableDockModules.FirstOrDefault();

    private DockRequestRow NewDockRow(DockOption? selected, int count = 1)
    {
        var row = new DockRequestRow(AvailableDockModules, selected, count) { Changed = OnDocksChanged };
        return row;
    }

    private void OnDocksChanged() => UpdatePlanSummary();

    /// <summary>(Re)builds the available storage-module list from the catalog, collapsing macro dupes.</summary>
    private void LoadStorageOptions()
    {
        AvailableStorageModules.Clear();
        var sorted = _modules.GetStorageModules()
            .OrderBy(m => m.CargoType.ToString(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => SizeSortKey(m.Size))
            .ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Macro, StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in sorted)
        {
            var key = string.IsNullOrEmpty(module.Macro) ? "id:" + module.Id : "macro:" + module.Macro;
            if (seen.Add(key))
            {
                AvailableStorageModules.Add(new DockOption(module));
            }
        }

        // Re-point existing rows at the equivalent module in the new catalog; drop rows whose module
        // no longer exists. Preserve the user-edited latch.
        var stale = new List<StorageRow>();
        foreach (var row in Storage)
        {
            var match = AvailableStorageModules.FirstOrDefault(m => SameModule(m, row.SelectedModule));
            if (match is null)
            {
                stale.Add(row);
            }
            else
            {
                row.SetSilently(match, row.Count);
            }
        }

        foreach (var row in stale)
        {
            Storage.Remove(row);
        }
    }

    private StorageRow NewStorageRow(DockOption? selected, int count = 1)
    {
        var row = new StorageRow(AvailableStorageModules, selected, count) { Changed = OnStorageChanged };
        return row;
    }

    /// <summary>
    /// Called whenever a storage row changes. User edits (<paramref name="userEdit"/> true) latch the
    /// storage as customised so later recomputes stop overwriting it; programmatic seeding does not.
    /// </summary>
    private void OnStorageChanged(bool userEdit)
    {
        if (userEdit)
        {
            _autoStorage = false;
        }

        UpdatePlanSummary();
    }

    /// <summary>
    /// Reseeds <see cref="Storage"/> from the calculated <see cref="StoragePlanner"/> output, unless
    /// the user has manually customised it (then the existing rows are kept).
    /// </summary>
    private void RefreshStorageFromCalculation()
    {
        if (!_autoStorage || _lastResult is null)
        {
            return;
        }

        // Storage defaults to the chosen species' faction (the user can override afterwards).
        // Build modules (wharf/shipyard) produce nothing to size storage from, so guarantee at least
        // one container store whenever any are present; the user can scale it up to the build size.
        var containerFloor = GetSelectedBuildModules().Count > 0 ? 1 : 0;
        var planned = new StoragePlanner(_wares, _modules)
            .Plan(_lastResult, preferredFaction: WorkforceFaction, minContainerModules: containerFloor);

        Storage.Clear();
        foreach (var item in planned)
        {
            var option = AvailableStorageModules.FirstOrDefault(m => SameModule(m, new DockOption(item.Module)))
                         ?? new DockOption(item.Module);
            Storage.Add(NewStorageRow(option, item.Count));
        }
    }

    /// <summary>
    /// The resolved storage modules (with counts) to fold into the assembled module list for layout
    /// and export. Rows with no module or a non-positive count are excluded.
    /// </summary>
    public IReadOnlyList<LayoutItem> GetSelectedStorage() =>
        Storage
            .Where(s => s.SelectedModule is not null && s.Count > 0)
            .Select(s => new LayoutItem(s.SelectedModule!.Module, s.Count))
            .ToList();


    [ObservableProperty]
    private string _title = "X4 Station Builder";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    private bool _isScanning;

    [ObservableProperty]
    private string _status = "No scan yet. Click \"Scan Game Folder\" to extract modules and wares.";

    [ObservableProperty]
    private double _progressFraction;

    [ObservableProperty]
    private string _progressStage = string.Empty;

    [ObservableProperty]
    private string? _gamePath;

    [ObservableProperty]
    private string _dlcSummary = string.Empty;

    [ObservableProperty]
    private string _resultSummary = string.Empty;

    // ---- Planner state ----

    /// <summary>Producible wares matching the current <see cref="PickerSearch"/> filter.</summary>
    public ObservableCollection<WarePickerItem> PickerItems { get; } = [];

    /// <summary>Wares the user wants produced, with editable quantities and faction.</summary>
    public ObservableCollection<DesiredWareRow> DesiredWares { get; } = [];

    /// <summary>Build modules (wharfs/shipyards) the user wants placed, with editable counts.</summary>
    public ObservableCollection<BuildModuleRow> BuildModules { get; } = [];

    /// <summary>True when at least one build module has been added (drives the build-module UI).</summary>
    public bool HasBuildModules => BuildModules.Count > 0;

    /// <summary>Resolved production modules required to satisfy the desired wares.</summary>
    public ObservableCollection<RequiredModuleRow> RequiredFactoryGroups { get; } = [];

    /// <summary>Raw (non-producible) resources consumed by the plan.</summary>
    public ObservableCollection<RawResourceRow> RawResources { get; } = [];

    /// <summary>Habitat modules required to house the workforce (workforce mode only).</summary>
    public ObservableCollection<HabitatRequirement> Habitats { get; } = [];

    /// <summary>Factions whose workforce food/medical needs can be modelled.</summary>
    public ObservableCollection<string> WorkforceFactions { get; } = [];

    /// <summary>Dock/pier options the user can choose from (sorted, from the catalog).</summary>
    public ObservableCollection<DockOption> AvailableDockModules { get; } = [];

    /// <summary>User-specified docking modules to include in the station.</summary>
    public ObservableCollection<DockRequestRow> Docks { get; } = [];

    /// <summary>Storage-module options the user can choose from (sorted, from the catalog).</summary>
    public ObservableCollection<DockOption> AvailableStorageModules { get; } = [];

    /// <summary>
    /// Editable storage modules to include in the station. Auto-seeded from <see cref="StoragePlanner"/>
    /// while <see cref="_autoStorage"/> is set, then preserved once the user edits them.
    /// </summary>
    public ObservableCollection<StorageRow> Storage { get; } = [];

    /// <summary>
    /// The resolved docking modules (with counts) to fold into the assembled module list for the
    /// layout algorithm (Step 09) and XML export (Step 10). Rows with no module or a non-positive
    /// count are excluded.
    /// </summary>
    public IReadOnlyList<DockRequest> GetSelectedDocks() =>
        Docks
            .Where(d => d.SelectedModule is not null && d.Count > 0)
            .Select(d => new DockRequest(d.SelectedModule!.Module, d.Count))
            .ToList();

    [ObservableProperty]
    private WarePickerItem? _selectedPickerItem;

    [ObservableProperty]
    private string _pickerSearch = string.Empty;

    [ObservableProperty]
    private DesiredWareRow? _selectedDesiredWare;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveBuildModuleCommand))]
    private BuildModuleRow? _selectedBuildModule;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveDockCommand))]
    private DockRequestRow? _selectedDock;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveStorageCommand))]
    private StorageRow? _selectedStorage;

    /// <summary>True while <see cref="Storage"/> tracks the calculation; cleared on the first user edit.</summary>
    private bool _autoStorage = true;

    [ObservableProperty]
    private bool _workforceEnabled;

    [ObservableProperty]
    private bool _produceWorkforceSupplies = true;

    [ObservableProperty]
    private string _workforceFaction = "Argon";

    [ObservableProperty]
    private int _totalWorkers;

    [ObservableProperty]
    private string _planSummary = "Add a ware to begin planning.";

    [ObservableProperty]
    private string _planName = string.Empty;

    /// <summary>
    /// True while the plan name is still auto-derived from the desired wares (so it refreshes on each
    /// recompute). Set false once the user types a custom name; reset to true if they clear the box.
    /// </summary>
    private bool _autoPlanName = true;

    /// <summary>Guards programmatic <see cref="PlanName"/> writes so they aren't treated as user edits.</summary>
    private bool _settingPlanName;

    private void SetPlanNameInternal(string value)
    {
        _settingPlanName = true;
        PlanName = value;
        _settingPlanName = false;
    }

    partial void OnPlanNameChanged(string value)
    {
        if (_settingPlanName)
        {
            return;
        }

        // A user edit: blank reverts to auto-naming, anything else pins the manual name.
        _autoPlanName = string.IsNullOrWhiteSpace(value);
    }

    [ObservableProperty]
    private string _warnings = string.Empty;

    partial void OnPickerSearchChanged(string value) => ApplyPickerFilter();

    partial void OnWorkforceEnabledChanged(bool value)
    {
        // Hold each desired ware's module count fixed; its output rises/falls with staffing.
        _suspendRecompute = true;
        foreach (var row in DesiredWares)
        {
            row.WorkforceEnabled = value;
        }

        _suspendRecompute = false;
        Recompute();
    }

    partial void OnProduceWorkforceSuppliesChanged(bool value) => Recompute();

    partial void OnWorkforceFactionChanged(string value) => Recompute();

    private void ApplyPickerFilter()
    {
        var search = PickerSearch?.Trim();
        var matches = string.IsNullOrEmpty(search)
            ? _allPickerItems
            : _allPickerItems.Where(i =>
                i.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase));

        PickerItems.Clear();
        foreach (var item in matches)
        {
            PickerItems.Add(item);
        }
    }

    private bool CanAddWare => SelectedPickerItem is not null;

    [RelayCommand(CanExecute = nameof(CanAddWare))]
    private void AddWare()
    {
        var pick = SelectedPickerItem;
        if (pick is null)
        {
            return;
        }

        if (pick.Kind == PickerKind.BuildModule)
        {
            AddBuildModule(pick.BuildModule);
            return;
        }

        var ware = _wares.GetByName(pick.WareName);
        if (ware is null || !ware.IsProducible)
        {
            return;
        }

        // The picked entry carries its specific production module + the recipe faction to use. Fall
        // back to the ware's default method when an entry has no faction.
        var faction = pick.Faction is not null && ware.RecipesByFaction.ContainsKey(pick.Faction)
            ? pick.Faction
            : ware.DefaultFaction is not null && ware.RecipesByFaction.ContainsKey(ware.DefaultFaction)
                ? ware.DefaultFaction
                : ware.ProducibleByFactions[0];

        var moduleId = pick.ProductionModule?.Id;

        // Allow the same ware under different production modules (e.g. Energy vs Terran Energy Cell
        // Production); dedup on the (ware, chosen module) pair.
        if (DesiredWares.Any(d =>
                string.Equals(d.WareName, ware.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(d.PreferredModuleId, moduleId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var moduleName = pick.ProductionModule?.Name;
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            moduleName = _modules.GetProductionModuleName(ware, faction);
        }

        var row = new DesiredWareRow(ware, faction, stations: 1, WorkforceEnabled, moduleName, moduleId);
        row.Changed = Recompute;
        DesiredWares.Add(row); // triggers Recompute via CollectionChanged
    }

    private void AddBuildModule(StationModule? module)
    {
        if (module is null)
        {
            return;
        }

        if (BuildModules.Any(b => SameBuildModule(b.Module, module)))
        {
            return;
        }

        var row = new BuildModuleRow(module);
        row.Changed = () => { UpdatePlanSummary(); Recompute(); };
        BuildModules.Add(row);
        Recompute();
        UpdatePlanSummary();
    }

    [RelayCommand]
    private void RemoveBuildModule(BuildModuleRow? row)
    {
        row ??= SelectedBuildModule;
        if (row is not null)
        {
            BuildModules.Remove(row);
            UpdatePlanSummary();
        }
    }

    private static bool SameBuildModule(StationModule a, StationModule b)
    {
        if (!string.IsNullOrEmpty(a.Macro) && !string.IsNullOrEmpty(b.Macro))
        {
            return string.Equals(a.Macro, b.Macro, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The resolved build modules (with counts) to fold into the layout as body modules.</summary>
    public IReadOnlyList<LayoutItem> GetSelectedBuildModules() =>
        BuildModules
            .Where(b => b.Count > 0)
            .Select(b => new LayoutItem(b.Module, b.Count))
            .ToList();

    /// <summary>
    /// Total workers employed by the selected build modules (wharf/shipyard fabrication bays), or 0
    /// when workforce is disabled. Folded into the production calculation so habitats and the
    /// workforce's food/medical supplies are sized to feed them.
    /// </summary>
    private int BuildModuleWorkforce() =>
        WorkforceEnabled
            ? BuildModules.Where(b => b.Count > 0).Sum(b => b.Module.WorkforceCapacity * b.Count)
            : 0;

    [RelayCommand]
    private void RemoveWare(DesiredWareRow? row)
    {
        row ??= SelectedDesiredWare;
        if (row is not null)
        {
            DesiredWares.Remove(row);
        }
    }

    partial void OnSelectedPickerItemChanged(WarePickerItem? value) => AddWareCommand.NotifyCanExecuteChanged();

    /// <summary>Recomputes the production chain from the current desired wares and options.</summary>
    public void Recompute()
    {
        if (_suspendRecompute)
        {
            return;
        }

        var desired = DesiredWares
            .Where(d => d.ItemsPerHour > 0)
            .Select(d => new DesiredWare(d.WareName, d.ItemsPerHour, d.SelectedFaction, d.PreferredModuleId))
            .ToList();

        var options = new ProductionOptions
        {
            WorkforceEnabled = WorkforceEnabled,
            WorkforceFaction = WorkforceFaction,
            ProduceWorkforceSupplies = ProduceWorkforceSupplies,
            ExtraWorkforce = BuildModuleWorkforce(),
            PreferredModuleFaction = WorkforceFaction,
        };

        var result = _calculator.Calculate(desired, options);
        _lastResult = result;

        RequiredFactoryGroups.Clear();
        foreach (var group in result.RequiredFactoryGroups)
        {
            // Prefer the specific chosen module's name (e.g. "Terran Energy Cell Production").
            var moduleName = (group.PreferredModuleId is not null
                                 ? _modules.GetById(group.PreferredModuleId)?.Name
                                 : null)
                             ?? _modules.GetProductionModuleName(group.Ware, group.Faction);

            RequiredFactoryGroups.Add(new RequiredModuleRow(
                moduleName,
                group.Faction,
                group.StationCountCeil,
                group.ItemCount,
                group.Workers));
        }

        RawResources.Clear();
        foreach (var (ware, rate) in result.TotalRawResources.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
        {
            RawResources.Add(new RawResourceRow(ware, rate));
        }

        Habitats.Clear();
        foreach (var habitat in result.Habitats)
        {
            Habitats.Add(habitat);
        }

        RefreshStorageFromCalculation();

        TotalWorkers = result.TotalWorkers;

        if (_autoPlanName)
        {
            SetPlanNameInternal(DefaultPlanName());
        }

        UpdatePlanSummary();

        var problems = new List<string>();
        if (result.UnproducibleDesiredWares.Count > 0)
        {
            problems.Add("Cannot produce: " + string.Join(", ", result.UnproducibleDesiredWares));
        }

        if (result.CyclicWares.Count > 0)
        {
            problems.Add("Production cycle halted for: " + string.Join(", ", result.CyclicWares));
        }

        Warnings = string.Join("  ", problems);
    }

    /// <summary>Rebuilds the footer summary from the current production, habitat and dock state.</summary>
    private void UpdatePlanSummary()
    {
        var dockCount = GetSelectedDocks().Sum(d => d.Count);
        var buildModuleCount = GetSelectedBuildModules().Sum(b => b.Count);

        if (DesiredWares.Count == 0 && dockCount == 0 && buildModuleCount == 0)
        {
            PlanSummary = "Add a ware to begin planning.";
            return;
        }

        var moduleCount = RequiredFactoryGroups.Sum(g => g.Modules)
            + Habitats.Sum(h => h.Count)
            + dockCount
            + buildModuleCount;

        var summary = $"{moduleCount} module(s) across {RequiredFactoryGroups.Count} production type(s)";
        if (WorkforceEnabled)
        {
            summary += $", {TotalWorkers} worker(s), {Habitats.Sum(h => h.Count)} habitat(s)";
        }

        if (dockCount > 0)
        {
            summary += $", {dockCount} dock(s)";
        }

        if (buildModuleCount > 0)
        {
            summary += $", {buildModuleCount} build module(s)";
        }

        var storageCount = GetSelectedStorage().Sum(s => s.Count);
        if (storageCount > 0)
        {
            summary += $", {storageCount} storage module(s)";
        }

        PlanSummary = summary + ".";
    }

    private bool CanRemoveDock => SelectedDock is not null;

    [RelayCommand]
    private void AddDock()
    {
        if (AvailableDockModules.Count == 0)
        {
            return;
        }

        var row = NewDockRow(PreferredDefaultDock());
        Docks.Add(row);
        SelectedDock = row;
        UpdatePlanSummary();
    }

    [RelayCommand(CanExecute = nameof(CanRemoveDock))]
    private void RemoveDock(DockRequestRow? row)
    {
        row ??= SelectedDock;
        if (row is not null)
        {
            Docks.Remove(row);
            UpdatePlanSummary();
        }
    }

    private bool CanRemoveStorage => SelectedStorage is not null;

    [RelayCommand]
    private void AddStorage()
    {
        // Adding a row is a manual customisation: latch so recompute stops overwriting storage.
        _autoStorage = false;

        var preferred = AvailableStorageModules.FirstOrDefault();
        var row = NewStorageRow(preferred);
        Storage.Add(row);
        SelectedStorage = row;
        UpdatePlanSummary();
    }

    [RelayCommand(CanExecute = nameof(CanRemoveStorage))]
    private void RemoveStorage(StorageRow? row)
    {
        row ??= SelectedStorage;
        if (row is not null)
        {
            _autoStorage = false;
            Storage.Remove(row);
            UpdatePlanSummary();
        }
    }

    /// <summary>
    /// Lets tests substitute the Settings dialog. Returns true when the user saved, false on cancel.
    /// </summary>
    public Func<SettingsViewModel, bool> SettingsDialog { get; set; } = ShowSettingsDialog;

    [RelayCommand]
    private void OpenSettings()
    {
        var editor = new SettingsViewModel(_settings, AvailableDockModules);
        if (!SettingsDialog(editor))
        {
            return;
        }

        editor.ApplyTo(_settings);
        _settingsStore.Save(_settings);

        // Re-seed docks from the new defaults only when the user hasn't customised the plan's docks
        // yet (still on the auto-seeded set). Avoid clobbering deliberate edits.
        if (Docks.Count == 0)
        {
            SeedDefaultDocks();
            UpdatePlanSummary();
        }
    }

    private static bool ShowSettingsDialog(SettingsViewModel editor)
    {
        var window = new Views.SettingsWindow
        {
            DataContext = editor,
            Owner = System.Windows.Application.Current?.MainWindow,
        };
        return window.ShowDialog() == true;
    }

    [RelayCommand]
    private void GenerateBlueprint()
    {
        var docks = GetSelectedDocks();
        if (_lastResult is null || (RequiredFactoryGroups.Count == 0 && docks.Count == 0 && GetSelectedBuildModules().Count == 0))
        {
            Status = "Nothing to export yet — add a ware, dock, or build module first.";
            return;
        }

        try
        {
            var layout = BuildLayout();
            if (layout is null || layout.Modules.Count == 0)
            {
                Status = "Layout produced no modules to export.";
                return;
            }

            var name = string.IsNullOrWhiteSpace(PlanName) ? DefaultPlanName() : PlanName.Trim();
            var suggested = System.IO.Path.Combine(
                ResolveConstructionPlansDirectory(),
                MakeSafeFileName(name) + ".xml");

            var path = BlueprintFilePicker(suggested);
            if (path is null)
            {
                return;
            }

            var exporter = new BlueprintXmlExporter();
            var exportOptions = new BlueprintXmlExporter.ExportOptions
            {
                PlanName = name,
                Dlcs = _dataStore.LoadMetadata()?.Dlcs,
            };
            exporter.ExportToFile(layout, exportOptions, path);

            // Round-trip sanity: confirm the written file parses as valid XML.
            _ = System.Xml.Linq.XDocument.Load(path);

            Status = $"Blueprint saved to {path}. Import it in-game via the station Construction menu.";
        }
        catch (Exception ex)
        {
            Status = $"Blueprint export failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Builds the positioned station layout from the current planner state (production result, docks,
    /// storage), or null when there's nothing to lay out. Shared by blueprint export and the 3D preview.
    /// </summary>
    private LayoutResult? BuildLayout()
    {
        if (_lastResult is null)
        {
            return null;
        }

        var docks = GetSelectedDocks();
        var storage = GetSelectedStorage();
        var buildModules = GetSelectedBuildModules();
        var extraBodies = storage.Concat(buildModules).ToList();
        var layoutInput = StationLayoutBuilder.Build(_lastResult, _modules, docks, extraBodies, WorkforceFaction);
        return new StationLayoutEngine().Layout(layoutInput);
    }

    /// <summary>Opens the 3D preview window, building the layout on demand. Refresh rebuilds it.</summary>
    [RelayCommand]
    private void OpenPreview()
    {
        if (_lastResult is null ||
            (RequiredFactoryGroups.Count == 0 && GetSelectedDocks().Count == 0 && GetSelectedBuildModules().Count == 0))
        {
            Status = "Nothing to preview yet — add a ware, dock, or build module first.";
            return;
        }

        ShowPreviewWindow(new PreviewViewModel(BuildLayout));
    }

    /// <summary>Lets tests substitute the preview window; shows it non-modally by default.</summary>
    public Action<PreviewViewModel> ShowPreviewWindow { get; set; } = static vm =>
    {
        var window = new Views.PreviewWindow(vm)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
        };
        window.Show();
    };


    /// <summary>A sensible default plan name derived from the first desired ware, with the user's
    /// configured name prefix prepended when set.</summary>
    private string DefaultPlanName()
    {
        var ware = DesiredWares.FirstOrDefault()?.WareName;
        var baseName = string.IsNullOrWhiteSpace(ware) ? "X4 Factory" : $"{ware} Factory";

        var prefix = _settings.StationNamePrefix?.Trim();
        return string.IsNullOrEmpty(prefix) ? baseName : $"{prefix} {baseName}";
    }

    /// <summary>Lets tests substitute the Save dialog; returns the chosen path or null on cancel.</summary>
    public Func<string, string?> BlueprintFilePicker { get; set; } = SaveBlueprint;

    /// <summary>
    /// Resolves the default local <c>Constructionplans</c> output folder, creating it on demand.
    /// Prefers a folder beside the executable; falls back to per-user app-data if that isn't writable.
    /// We never auto-write to the game folder — the user imports the saved plan themselves.
    /// </summary>
    private static string ResolveConstructionPlansDirectory()
    {
        var beside = System.IO.Path.Combine(AppContext.BaseDirectory, "Constructionplans");
        try
        {
            System.IO.Directory.CreateDirectory(beside);
            return beside;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            var fallback = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "X4StationBuilder",
                "Constructionplans");
            System.IO.Directory.CreateDirectory(fallback);
            return fallback;
        }
    }

    private static string MakeSafeFileName(string name)
    {
        var safe = string.Join("_", name.Split(System.IO.Path.GetInvalidFileNameChars()));
        return string.IsNullOrWhiteSpace(safe) ? "X4 Station" : safe.Trim();
    }

    private static string? SaveBlueprint(string suggestedPath)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save construction plan",
            Filter = "X4 construction plan (*.xml)|*.xml|All files (*.*)|*.*",
            DefaultExt = ".xml",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = System.IO.Path.GetFileName(suggestedPath),
        };

        var dir = System.IO.Path.GetDirectoryName(suggestedPath);
        if (!string.IsNullOrWhiteSpace(dir) && System.IO.Directory.Exists(dir))
        {
            dialog.InitialDirectory = dir;
        }

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <summary>Lets the caller substitute the folder-picker in tests; returns null on cancel.</summary>
    public Func<string?, string?> FolderPicker { get; set; } = PickFolder;

    private bool CanScan => !IsScanning;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        var path = GamePath;
        if (!GameFolderLocator.IsValidGameFolder(path))
        {
            path = FolderPicker(path);
            if (path is null)
            {
                return;
            }

            if (!GameFolderLocator.IsValidGameFolder(path))
            {
                Status = "That folder is not a valid X4 install (missing 01.cat or extensions).";
                return;
            }
        }

        GamePath = path;
        _settings.GamePath = path;
        _settingsStore.Save(_settings);

        IsScanning = true;
        Status = "Scanning…";
        ProgressFraction = 0;

        var progress = new Progress<ScanProgress>(p =>
        {
            ProgressStage = p.Stage;
            ProgressFraction = p.Fraction;
        });

        try
        {
            var result = await Task.Run(() => _scanner.Scan(path!, progress));
            _dataStore.Save(result);
            RebuildFromScannedData();
            ApplySummary(result.Metadata);
            Status = $"Scan complete. Saved to {_dataStore.DataDirectory}.";
        }
        catch (Exception ex)
        {
            Status = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            ProgressStage = string.Empty;
        }
    }

    /// <summary>Rebuilds repository, catalog, calculator and picker from freshly scanned data.</summary>
    private void RebuildFromScannedData()
    {
        _wares = WareRepository.CreatePreferringScanned(_dataStore);
        _modules = ModuleCatalog.CreatePreferringScanned(_dataStore);
        _calculator = new ProductionCalculator(_wares, _modules);

        // Drop desired wares whose names no longer exist in the new data, then recompute.
        var stale = DesiredWares
            .Where(d => _wares.GetByName(d.WareName) is not { IsProducible: true })
            .ToList();
        foreach (var row in stale)
        {
            DesiredWares.Remove(row);
        }

        LoadDataDependentState();
        Recompute();
    }

    private void ApplySummary(ScanMetadata metadata)
    {
        var dlcCount = metadata.Dlcs.Count(d => d.IsOfficialDlc);
        ResultSummary =
            $"Found {metadata.ModuleCount} modules and {metadata.WareCount} wares across base + {dlcCount} DLC(s).";
        DlcSummary = metadata.Dlcs.Count == 0
            ? "No extensions detected."
            : string.Join(", ", metadata.Dlcs.Select(d => d.Name ?? d.Id));
    }

    private static string? PickFolder(string? initial)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select your X4: Foundations install folder",
        };

        if (!string.IsNullOrWhiteSpace(initial) && System.IO.Directory.Exists(initial))
        {
            dialog.InitialDirectory = initial;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
