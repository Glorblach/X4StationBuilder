using X4StationBuilder.App.ViewModels;
using X4StationBuilder.Core.Services;

namespace X4StationBuilder.Tests;

public class MainViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly MainViewModel _vm;

    public MainViewModelTests()
    {
        // Empty temp dirs → no scanned data → repository/catalog fall back to bundled maps.
        _tempDir = Path.Combine(Path.GetTempPath(), "x4sb-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var settings = new SettingsStore(Path.Combine(_tempDir, "settings.json"));
        var dataStore = new ScannedDataStore(Path.Combine(_tempDir, "ScannedData"));
        _vm = new MainViewModel(settings, dataStore, new GameFolderScanner());
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private void AddWare(string wareName)
    {
        _vm.SelectedPickerItem = _vm.PickerItems.First(
            p => string.Equals(p.WareName, wareName, StringComparison.OrdinalIgnoreCase));
        _vm.AddWareCommand.Execute(null);
    }

    [Fact]
    public void Construction_PopulatesPickerWithProducibleWares()
    {
        Assert.NotEmpty(_vm.PickerItems);
        Assert.Contains(_vm.PickerItems, p => p.WareName.Equals("Antimatter cell", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(_vm.DesiredWares);
        Assert.Empty(_vm.RequiredFactoryGroups);
    }

    [Fact]
    public void AddWare_PopulatesRequiredModulesAndRawResources()
    {
        AddWare("Antimatter cell");

        var row = Assert.Single(_vm.DesiredWares);
        Assert.True(row.ItemsPerHour > 0);

        Assert.Contains(_vm.RequiredFactoryGroups, g => g.ModuleName.Contains("Antimatter cell", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(_vm.RequiredFactoryGroups, g => g.ModuleName.Contains("Energy cell", StringComparison.OrdinalIgnoreCase));
        // Hydrogen is a leaf resource → appears as a raw resource, not a module.
        Assert.Contains(_vm.RawResources, r => r.WareName.Equals("Hydrogen", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EditingQuantity_Recomputes()
    {
        AddWare("Antimatter cell");
        var row = _vm.DesiredWares.Single();

        var baselineModules = _vm.RequiredFactoryGroups
            .First(g => g.ModuleName.Contains("Antimatter cell", StringComparison.OrdinalIgnoreCase))
            .Modules;

        row.ItemsPerHour *= 5;

        var scaledModules = _vm.RequiredFactoryGroups
            .First(g => g.ModuleName.Contains("Antimatter cell", StringComparison.OrdinalIgnoreCase))
            .Modules;

        Assert.True(scaledModules > baselineModules);
    }

    [Fact]
    public void EditingStations_UpdatesItemsPerHour()
    {
        AddWare("Antimatter cell");
        var row = _vm.DesiredWares.Single();

        row.Stations = 3;

        Assert.True(row.ItemsPerHour > 0);
        // Stations × per-cycle amount = items/hour; round-trips back to ~3 stations.
        Assert.Equal(3, row.Stations, 3);
    }

    [Fact]
    public void EnablingWorkforce_AddsWorkersHabitatsAndFood()
    {
        AddWare("Antimatter cell");
        Assert.Equal(0, _vm.TotalWorkers);
        Assert.Empty(_vm.Habitats);

        _vm.WorkforceEnabled = true;

        Assert.True(_vm.TotalWorkers > 0);
        Assert.NotEmpty(_vm.Habitats);
        // Workforce food/medical chain pulls in additional production (e.g. food, water, medical).
        Assert.Contains(_vm.RequiredFactoryGroups,
            g => g.ModuleName.Contains("Medical", StringComparison.OrdinalIgnoreCase)
                 || g.ModuleName.Contains("Food", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnablingWorkforce_KeepsDesiredModuleCount_AndIncreasesOutput()
    {
        AddWare("Antimatter cell");
        var row = _vm.DesiredWares.Single();
        row.Stations = 5;

        var itemsBefore = row.ItemsPerHour;
        var desiredModulesBefore = _vm.RequiredFactoryGroups
            .First(g => g.ModuleName.Contains("Antimatter cell", StringComparison.OrdinalIgnoreCase))
            .Modules;

        _vm.WorkforceEnabled = true;

        // Same number of desired factories as set; the output (and dependencies) increase instead.
        Assert.Equal(5, row.Stations, 3);
        Assert.True(row.ItemsPerHour > itemsBefore);

        var desiredModulesAfter = _vm.RequiredFactoryGroups
            .First(g => g.ModuleName.Contains("Antimatter cell", StringComparison.OrdinalIgnoreCase))
            .Modules;
        Assert.Equal(desiredModulesBefore, desiredModulesAfter);
    }

    [Fact]
    public void TerranSpecies_DefaultsEnergyCellIntermediateToTerranModule()
    {
        // Antimatter cell consumes energy cells; the energy-cell intermediate has no explicit pick,
        // so it should default to the selected species' module variant.
        AddWare("Antimatter cell");

        _vm.WorkforceFaction = "Terran";
        Assert.Contains(_vm.RequiredFactoryGroups, g => g.ModuleName == "Terran Energy Cell Production");

        _vm.WorkforceFaction = "Argon";
        Assert.DoesNotContain(_vm.RequiredFactoryGroups, g => g.ModuleName == "Terran Energy Cell Production");
    }

    [Fact]
    public void DisablingProduceSupplies_RemovesFoodMedicalModules()
    {
        AddWare("Antimatter cell");
        _vm.WorkforceEnabled = true;
        Assert.Contains(_vm.RequiredFactoryGroups,
            g => g.ModuleName.Contains("Food", StringComparison.OrdinalIgnoreCase)
                 || g.ModuleName.Contains("Medical", StringComparison.OrdinalIgnoreCase));

        _vm.ProduceWorkforceSupplies = false;

        Assert.DoesNotContain(_vm.RequiredFactoryGroups,
            g => g.ModuleName.Contains("Food", StringComparison.OrdinalIgnoreCase)
                 || g.ModuleName.Contains("Medical", StringComparison.OrdinalIgnoreCase));
        // Workforce still present (workers + habitats), supplies now imported.
        Assert.True(_vm.TotalWorkers > 0);
        Assert.NotEmpty(_vm.Habitats);
    }

    [Fact]
    public void RemoveWare_ClearsOutputs()
    {
        AddWare("Antimatter cell");
        Assert.NotEmpty(_vm.RequiredFactoryGroups);

        _vm.RemoveWareCommand.Execute(_vm.DesiredWares.Single());

        Assert.Empty(_vm.DesiredWares);
        Assert.Empty(_vm.RequiredFactoryGroups);
        Assert.Empty(_vm.RawResources);
    }

    [Fact]
    public void Construction_SeedsDefaultDock()
    {
        Assert.NotEmpty(_vm.AvailableDockModules);
        var dock = Assert.Single(_vm.Docks);
        Assert.NotNull(dock.SelectedModule);
        Assert.Equal(1, dock.Count);

        var selected = Assert.Single(_vm.GetSelectedDocks());
        Assert.Equal(dock.SelectedModule!.Module, selected.Module);
        Assert.Equal(1, selected.Count);
    }

    [Fact]
    public void AddAndRemoveDock_UpdatesSelection()
    {
        var initialCount = _vm.Docks.Count;

        _vm.AddDockCommand.Execute(null);
        Assert.Equal(initialCount + 1, _vm.Docks.Count);

        var added = _vm.Docks.Last();
        _vm.RemoveDockCommand.Execute(added);
        Assert.Equal(initialCount, _vm.Docks.Count);
        Assert.DoesNotContain(added, _vm.Docks);
    }

    [Fact]
    public void NegativeDockCount_ClampedToZero_AndExcludedFromSelection()
    {
        var dock = _vm.Docks.Single();

        dock.Count = -3;

        Assert.Equal(0, dock.Count);
        Assert.Empty(_vm.GetSelectedDocks());
    }

    [Fact]
    public void GetSelectedDocks_ReflectsModuleAndCount()
    {
        var dock = _vm.Docks.Single();
        dock.SelectedModule = _vm.AvailableDockModules.First();
        dock.Count = 4;

        var selected = Assert.Single(_vm.GetSelectedDocks());
        Assert.Equal(_vm.AvailableDockModules.First().Module, selected.Module);
        Assert.Equal(4, selected.Count);
    }

    [Fact]
    public void DockOptions_AreSortedByFactionThenDockBeforePier()
    {
        // Faction key derived from the macro/id token (matches the VM's grouping logic).
        static string Key(X4StationBuilder.Core.Models.StationModule m)
        {
            var source = string.IsNullOrEmpty(m.Macro) ? m.Id : m.Macro;
            var parts = (source ?? string.Empty).Split('_');
            if (parts.Length >= 2)
            {
                return parts[1];
            }

            return m.Factions.Count > 0 ? m.Factions[0] : "\uffff";
        }

        // Faction groups are contiguous (no faction key appears, breaks, then reappears).
        var keys = _vm.AvailableDockModules.Select(o => Key(o.Module)).ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? prev = null;
        foreach (var key in keys)
        {
            if (!string.Equals(key, prev, StringComparison.OrdinalIgnoreCase))
            {
                Assert.True(seen.Add(key), $"Faction '{key}' is not contiguous in the dock list.");
                prev = key;
            }
        }
    }

    [Fact]
    public void DockOptions_ContainNoDuplicateMacros()
    {
        var macros = _vm.AvailableDockModules
            .Where(o => !string.IsNullOrEmpty(o.Module.Macro))
            .Select(o => o.Module.Macro!)
            .ToList();

        Assert.Equal(macros.Count, macros.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void DockOption_Label_IncludesSizeAndMacro()
    {
        var option = _vm.AvailableDockModules.First(o => !string.IsNullOrEmpty(o.Module.Macro));

        Assert.Contains(option.Module.Size.ToString(), option.Label);
        Assert.Contains(option.Module.Macro!, option.Label);
    }

    [Fact]
    public void GenerateBlueprint_WritesParseablePlanFile()
    {
        AddWare("Antimatter cell");

        var outPath = Path.Combine(_tempDir, "plan.xml");
        string? suggested = null;
        _vm.BlueprintFilePicker = s => { suggested = s; return outPath; };

        _vm.GenerateBlueprintCommand.Execute(null);

        Assert.True(File.Exists(outPath));
        Assert.EndsWith(".xml", suggested);
        var doc = System.Xml.Linq.XDocument.Load(outPath);
        Assert.Equal("plans", doc.Root!.Name.LocalName);
        Assert.NotEmpty(doc.Root!.Element("plan")!.Elements("entry"));
        Assert.Contains("Blueprint saved", _vm.Status);
    }

    [Fact]
    public void GenerateBlueprint_UsesPlanNameForFileNameSuggestion()
    {
        AddWare("Antimatter cell");
        _vm.PlanName = "My Test Station";

        string? suggested = null;
        _vm.BlueprintFilePicker = s => { suggested = s; return null; }; // user cancels

        _vm.GenerateBlueprintCommand.Execute(null);

        Assert.NotNull(suggested);
        Assert.Equal("My Test Station.xml", Path.GetFileName(suggested!));
    }

    [Fact]
    public void GenerateBlueprint_CancelledPicker_WritesNothing()
    {
        AddWare("Antimatter cell");
        _vm.BlueprintFilePicker = _ => null;

        _vm.GenerateBlueprintCommand.Execute(null);

        Assert.DoesNotContain("Blueprint saved", _vm.Status);
    }

    [Fact]
    public void GenerateBlueprint_WithNothingToExport_DoesNotPrompt()
    {
        var prompted = false;
        _vm.BlueprintFilePicker = s => { prompted = true; return null; };

        _vm.GenerateBlueprintCommand.Execute(null);

        Assert.False(prompted);
        Assert.Contains("Nothing to export", _vm.Status);
    }

    [Fact]
    public void AddWare_AutoFillsStorageFromCalculation()
    {
        Assert.Empty(_vm.Storage);

        AddWare("Antimatter cell");

        Assert.NotEmpty(_vm.AvailableStorageModules);
        Assert.NotEmpty(_vm.Storage);
        Assert.All(_vm.Storage, s => Assert.NotNull(s.SelectedModule));
        Assert.NotEmpty(_vm.GetSelectedStorage());
    }

    [Fact]
    public void EditingStorageCount_LatchesAndSurvivesRecompute()
    {
        AddWare("Antimatter cell");
        var row = _vm.Storage.First();

        row.Count = 99; // manual edit → latches auto-fill off

        // Trigger a recompute that would otherwise re-seed storage from the calculation.
        _vm.DesiredWares.Single().ItemsPerHour *= 3;

        Assert.Contains(_vm.Storage, s => s.Count == 99);
        Assert.Contains(_vm.GetSelectedStorage(), s => s.Count == 99);
    }

    [Fact]
    public void AddStorage_AddsRow_AndStopsAutoOverwrite()
    {
        AddWare("Antimatter cell");
        var seededCount = _vm.Storage.Count;

        _vm.AddStorageCommand.Execute(null);
        Assert.Equal(seededCount + 1, _vm.Storage.Count);

        // Recompute must not wipe the manual addition.
        _vm.DesiredWares.Single().ItemsPerHour *= 2;
        Assert.Equal(seededCount + 1, _vm.Storage.Count);
    }

    [Fact]
    public void RemoveStorage_RemovesRow_AndIsExcludedFromSelection()
    {
        AddWare("Antimatter cell");
        var row = _vm.Storage.First();

        _vm.RemoveStorageCommand.Execute(row);

        Assert.DoesNotContain(row, _vm.Storage);
        Assert.DoesNotContain(_vm.GetSelectedStorage(), s => ReferenceEquals(s.Module, row.SelectedModule?.Module) && s.Count == row.Count);
    }

    [Fact]
    public void GetSelectedStorage_ReflectsModuleAndCount()
    {
        AddWare("Antimatter cell");
        var row = _vm.Storage.First();
        row.SelectedModule = _vm.AvailableStorageModules.First();
        row.Count = 7;

        Assert.Contains(_vm.GetSelectedStorage(),
            s => s.Module == _vm.AvailableStorageModules.First().Module && s.Count == 7);
    }

    [Fact]
    public void NegativeStorageCount_ClampedToZero_AndExcludedFromSelection()
    {
        AddWare("Antimatter cell");
        var row = _vm.Storage.First();

        row.Count = -5;

        Assert.Equal(0, row.Count);
        Assert.DoesNotContain(_vm.GetSelectedStorage(), s => ReferenceEquals(s.Module, row.SelectedModule!.Module) && s.Count <= 0);
    }

    /// <summary>Builds a fresh VM whose settings are seeded from <paramref name="settings"/>.</summary>
    private MainViewModel NewVmWithSettings(AppSettings settings)
    {
        var settingsPath = Path.Combine(_tempDir, "settings-" + Guid.NewGuid().ToString("N") + ".json");
        var store = new SettingsStore(settingsPath);
        store.Save(settings);
        var dataStore = new ScannedDataStore(Path.Combine(_tempDir, "ScannedData"));
        return new MainViewModel(store, dataStore, new GameFolderScanner());
    }

    [Fact]
    public void Construction_SeedsDocksFromConfiguredDefaults()
    {
        // Pick two real macros the catalog exposes, configure them as defaults.
        var available = _vm.AvailableDockModules
            .Where(o => !string.IsNullOrEmpty(o.Module.Macro))
            .Select(o => o.Module.Macro!)
            .Distinct()
            .Take(2)
            .ToList();
        Assert.NotEmpty(available);

        var defaults = available
            .Select((macro, i) => new DockDefault { Macro = macro, Count = i + 2 })
            .ToList();

        var vm = NewVmWithSettings(new AppSettings { DefaultDocks = defaults });

        Assert.Equal(defaults.Count, vm.Docks.Count);
        for (var i = 0; i < defaults.Count; i++)
        {
            Assert.Equal(defaults[i].Macro, vm.Docks[i].SelectedModule!.Module.Macro);
            Assert.Equal(defaults[i].Count, vm.Docks[i].Count);
        }
    }

    [Fact]
    public void Construction_FallsBackToSingleDock_WhenDefaultMacrosUnavailable()
    {
        var vm = NewVmWithSettings(new AppSettings
        {
            DefaultDocks = [new DockDefault { Macro = "no_such_macro", Count = 3 }],
        });

        var dock = Assert.Single(vm.Docks);
        Assert.NotNull(dock.SelectedModule);
    }

    [Fact]
    public void DefaultPlanName_IncludesConfiguredPrefix()
    {
        var vm = NewVmWithSettings(new AppSettings { StationNamePrefix = "NML-ARG" });

        vm.SelectedPickerItem = vm.PickerItems.First(
            p => p.WareName.Equals("Antimatter cell", StringComparison.OrdinalIgnoreCase));
        vm.AddWareCommand.Execute(null);

        Assert.StartsWith("NML-ARG ", vm.PlanName);
    }

    [Fact]
    public void AutoPlanName_RefreshesWhenWaresChange()
    {
        AddWare("Antimatter cell");
        Assert.Contains("Antimatter cell", _vm.PlanName, StringComparison.OrdinalIgnoreCase);

        // Clear and switch wares: the auto-derived name should follow the new ware.
        foreach (var row in _vm.DesiredWares.ToList())
        {
            _vm.RemoveWareCommand.Execute(row);
        }

        AddWare("Energy cell");

        Assert.Contains("Energy cell", _vm.PlanName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Antimatter", _vm.PlanName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManualPlanName_IsPreservedAcrossWareChanges()
    {
        AddWare("Antimatter cell");
        _vm.PlanName = "My Custom Name"; // user edit pins the name

        foreach (var row in _vm.DesiredWares.ToList())
        {
            _vm.RemoveWareCommand.Execute(row);
        }

        AddWare("Energy cell");

        Assert.Equal("My Custom Name", _vm.PlanName);
    }

    [Fact]
    public void ClearingPlanName_RevertsToAutoNaming()
    {
        AddWare("Antimatter cell");
        _vm.PlanName = "Manual"; // pin
        _vm.PlanName = string.Empty; // clearing reverts to auto

        foreach (var row in _vm.DesiredWares.ToList())
        {
            _vm.RemoveWareCommand.Execute(row);
        }

        AddWare("Energy cell");

        Assert.Contains("Energy cell", _vm.PlanName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OpenSettings_Save_PersistsPrefixAndDocks()
    {
        var settingsPath = Path.Combine(_tempDir, "persist-settings.json");
        var store = new SettingsStore(settingsPath);
        var dataStore = new ScannedDataStore(Path.Combine(_tempDir, "ScannedData"));
        var vm = new MainViewModel(store, dataStore, new GameFolderScanner());

        var macro = vm.AvailableDockModules.First(o => !string.IsNullOrEmpty(o.Module.Macro)).Module.Macro!;

        vm.SettingsDialog = editor =>
        {
            editor.StationNamePrefix = "NML-TST";
            editor.DefaultDocks.Clear();
            var option = editor.AvailableDockModules.First(o => o.Module.Macro == macro);
            editor.DefaultDocks.Add(new DockRequestRow(editor.AvailableDockModules, option, 5));
            return true; // user saves
        };

        vm.OpenSettingsCommand.Execute(null);

        var loaded = new SettingsStore(settingsPath).Load();
        Assert.Equal("NML-TST", loaded.StationNamePrefix);
        var saved = Assert.Single(loaded.DefaultDocks);
        Assert.Equal(macro, saved.Macro);
        Assert.Equal(5, saved.Count);
    }

    [Fact]
    public void OpenSettings_Cancel_DoesNotPersist()
    {
        var settingsPath = Path.Combine(_tempDir, "cancel-settings.json");
        var store = new SettingsStore(settingsPath);
        var dataStore = new ScannedDataStore(Path.Combine(_tempDir, "ScannedData"));
        var vm = new MainViewModel(store, dataStore, new GameFolderScanner());

        vm.SettingsDialog = editor => { editor.StationNamePrefix = "SHOULD-NOT-SAVE"; return false; };

        vm.OpenSettingsCommand.Execute(null);

        Assert.False(File.Exists(settingsPath));
    }

    [Fact]
    public void Picker_ExcludesWaresWithoutAProductionModule()
    {
        // Graphene is producible (has a recipe) but the bundled catalog has no module that makes
        // it, so it must not appear in the picker.
        Assert.DoesNotContain(_vm.PickerItems,
            p => p.Kind == PickerKind.Ware
                 && p.WareName.Equals("Graphene", StringComparison.OrdinalIgnoreCase));

        // Energy cell has a dedicated production module, so it stays.
        Assert.Contains(_vm.PickerItems,
            p => p.Kind == PickerKind.Ware
                 && p.WareName.Equals("Energy cell", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Picker_IncludesBuildModules()
    {
        Assert.Contains(_vm.PickerItems,
            p => p.Kind == PickerKind.BuildModule
                 && p.WareName.Contains("Ship Fabrication Bay", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AddBuildModule_AddsRowAndDoesNotCreateDesiredWare()
    {
        var pick = _vm.PickerItems.First(p => p.Kind == PickerKind.BuildModule);
        _vm.SelectedPickerItem = pick;
        _vm.AddWareCommand.Execute(null);

        var row = Assert.Single(_vm.BuildModules);
        Assert.Equal(1, row.Count);
        Assert.True(_vm.HasBuildModules);
        Assert.Empty(_vm.DesiredWares);

        var selected = Assert.Single(_vm.GetSelectedBuildModules());
        Assert.Equal(1, selected.Count);
        Assert.Same(pick.BuildModule, selected.Module);
    }

    [Fact]
    public void AddBuildModule_IsDeduplicated()
    {
        var pick = _vm.PickerItems.First(p => p.Kind == PickerKind.BuildModule);
        _vm.SelectedPickerItem = pick;
        _vm.AddWareCommand.Execute(null);
        _vm.AddWareCommand.Execute(null);

        Assert.Single(_vm.BuildModules);
    }

    [Fact]
    public void RemoveBuildModule_ClearsRowAndFlag()
    {
        var pick = _vm.PickerItems.First(p => p.Kind == PickerKind.BuildModule);
        _vm.SelectedPickerItem = pick;
        _vm.AddWareCommand.Execute(null);

        var row = Assert.Single(_vm.BuildModules);
        _vm.RemoveBuildModuleCommand.Execute(row);

        Assert.Empty(_vm.BuildModules);
        Assert.False(_vm.HasBuildModules);
        Assert.Empty(_vm.GetSelectedBuildModules());
    }

    [Fact]
    public void GetSelectedBuildModules_ExcludesNonPositiveCounts()
    {
        var pick = _vm.PickerItems.First(p => p.Kind == PickerKind.BuildModule);
        _vm.SelectedPickerItem = pick;
        _vm.AddWareCommand.Execute(null);

        _vm.BuildModules[0].Count = 0;

        Assert.Empty(_vm.GetSelectedBuildModules());
    }

    [Fact]
    public void BuildModule_WithWorkforce_AddsWorkersHabitatsAndStorage()
    {
        _vm.WorkforceEnabled = true;

        // A bare wharf/shipyard: a single staffed build module, no production wares.
        var pick = _vm.PickerItems.First(p =>
            p.Kind == PickerKind.BuildModule && p.BuildModule!.WorkforceCapacity > 0);
        _vm.SelectedPickerItem = pick;
        _vm.AddWareCommand.Execute(null);

        var perModule = pick.BuildModule!.WorkforceCapacity;

        // Build-module workforce drives the plan even with no desired wares.
        Assert.True(_vm.TotalWorkers >= perModule);
        Assert.NotEmpty(_vm.Habitats);
        Assert.True(_vm.Habitats.Sum(h => h.HousedWorkers) >= _vm.TotalWorkers);

        // Food/medical production is added to feed the workers.
        Assert.Contains(_vm.RequiredFactoryGroups, g => g.Modules > 0);

        // At least one container storage module is seeded for the wharf/shipyard.
        Assert.NotEmpty(_vm.Storage);
        Assert.NotEmpty(_vm.GetSelectedStorage());

        // Increasing the build-module count increases the workforce.
        var before = _vm.TotalWorkers;
        _vm.BuildModules[0].Count = 3;
        Assert.True(_vm.TotalWorkers > before);
        Assert.True(_vm.TotalWorkers >= perModule * 3);
    }

    [Fact]
    public void BuildModule_WorkforceDisabled_AddsNoWorkersOrHabitats()
    {
        Assert.False(_vm.WorkforceEnabled);

        var pick = _vm.PickerItems.First(p =>
            p.Kind == PickerKind.BuildModule && p.BuildModule!.WorkforceCapacity > 0);
        _vm.SelectedPickerItem = pick;
        _vm.AddWareCommand.Execute(null);

        Assert.Equal(0, _vm.TotalWorkers);
        Assert.Empty(_vm.Habitats);
    }

    [Fact]
    public void Picker_AssignsCategoryGroupHeaders()
    {
        var energyCell = _vm.PickerItems.First(
            p => p.Kind == PickerKind.Ware && p.WareName.Equals("Energy cell", StringComparison.OrdinalIgnoreCase));
        var antimatter = _vm.PickerItems.First(
            p => p.Kind == PickerKind.Ware && p.WareName.Equals("Antimatter cell", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("Energy", energyCell.GroupLabel);
        Assert.Equal("Refined Goods", antimatter.GroupLabel);
    }

    [Fact]
    public void Picker_OrdersWareGroupsBeforeBuildModules()
    {
        // Category groups (low order) must all precede the Build Module group in the flat list, so
        // the CollectionViewSource renders headers in the curated order.
        var lastWareIndex = -1;
        var firstBuildIndex = int.MaxValue;
        for (var i = 0; i < _vm.PickerItems.Count; i++)
        {
            if (_vm.PickerItems[i].Kind == PickerKind.Ware)
            {
                lastWareIndex = i;
            }
            else if (i < firstBuildIndex)
            {
                firstBuildIndex = i;
            }
        }

        Assert.True(lastWareIndex < firstBuildIndex);
        Assert.All(_vm.PickerItems.Where(p => p.Kind == PickerKind.BuildModule),
            p => Assert.Equal("Build Module", p.GroupLabel));
    }

    [Fact]
    public void Picker_ListsDistinctPlayerBuildableProductionModules()
    {
        // Energy cell has a generic module + a Terran module (+ a Xenon "Matrix Solar Panel" that the
        // player can't build) in the bundled seed.
        var energyEntries = _vm.PickerItems
            .Where(p => p.Kind == PickerKind.Ware
                        && p.WareName.Equals("Energy cell", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var terran = Assert.Single(energyEntries,
            p => p.DisplayName.Equals("Terran Energy Cell Production", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("prod_ter_energycells", terran.ProductionModule!.Id);

        // The generic entry is present, and the enemy (Xenon) module is excluded.
        Assert.Contains(energyEntries, p => p.ProductionModule!.Id == "prod_arg_energycells");
        Assert.DoesNotContain(energyEntries,
            p => p.DisplayName.Contains("Matrix Solar Panel", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(energyEntries, p => p.ProductionModule!.Id == "prod_xen_energycells");
    }

    [Fact]
    public void Picker_PrefixesBuildModulesWithFaction()
    {
        var build = _vm.PickerItems.First(p => p.Kind == PickerKind.BuildModule);

        Assert.StartsWith("Argon", build.DisplayName);
        Assert.Contains("Ship Fabrication Bay", build.DisplayName);
    }

    [Fact]
    public void Search_MatchesDisplayLabel()
    {
        _vm.PickerSearch = "Terran";

        Assert.Contains(_vm.PickerItems,
            p => p.DisplayName.StartsWith("Terran", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorkforceFactions_IncludeFactionsWithHabitats()
    {
        // The bundled seed has Argon and Terran habitats.
        Assert.Contains("Argon", _vm.WorkforceFactions);
        Assert.Contains("Terran", _vm.WorkforceFactions);
    }

    [Fact]
    public void SwitchingSpecies_UsesThatFactionsHabitatAndStorage()
    {
        _vm.WorkforceEnabled = true;
        AddWare("Antimatter cell"); // employs workers → habitats are produced

        _vm.WorkforceFaction = "Terran";

        // Habitat follows the chosen species.
        Assert.Contains(_vm.Habitats, h => h.Module.Id == "hab_ter");
        Assert.DoesNotContain(_vm.Habitats, h => h.Module.Id.StartsWith("hab_arg", StringComparison.OrdinalIgnoreCase));

        // Auto-seeded storage follows the chosen species too.
        Assert.Contains(_vm.Storage,
            s => s.SelectedModule!.Module.Factions.Contains("terran", StringComparer.OrdinalIgnoreCase));
    }
}
