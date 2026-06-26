using X4StationBuilder.Core.Data;
using X4StationBuilder.Core.Models;

namespace X4StationBuilder.Core.Services;

/// <summary>
/// Assembles a flat <see cref="StationLayout"/> (the input to <see cref="StationLayoutEngine"/>)
/// from a calculated <see cref="ProductionResult"/>, the chosen docks, and any extra body modules
/// (e.g. storage), resolving each production ware to its concrete module via the catalog.
/// </summary>
public static class StationLayoutBuilder
{
    /// <summary>Builds a layout input from a production result, docks, and optional extra bodies.</summary>
    /// <param name="result">The calculated production chain (provides production groups and habitats).</param>
    /// <param name="catalog">Module catalog used to resolve production wares → modules and connectors.</param>
    /// <param name="docks">Selected dock/pier requests to place on the outer shell.</param>
    /// <param name="extraBodies">Optional extra body modules (e.g. storage) to include.</param>
    /// <param name="preferredFaction">Station species; the engine prefers its structural connector.</param>
    public static StationLayout Build(
        ProductionResult result,
        ModuleCatalog catalog,
        IReadOnlyList<DockRequest>? docks = null,
        IEnumerable<LayoutItem>? extraBodies = null,
        string? preferredFaction = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(catalog);

        var bodies = new List<LayoutItem>();

        foreach (var group in result.RequiredFactoryGroups)
        {
            // Honour a user-chosen specific module (e.g. the Terran variant) when set; otherwise pick
            // the default module for the ware/faction.
            var module = (group.PreferredModuleId is not null ? catalog.GetById(group.PreferredModuleId) : null)
                         ?? catalog.GetProductionModule(group.Ware, group.Faction)
                         ?? catalog.GetProductionModule(group.Ware);
            if (module is null || group.StationCountCeil <= 0)
            {
                continue;
            }

            bodies.Add(new LayoutItem(module, group.StationCountCeil));
        }

        foreach (var habitat in result.Habitats)
        {
            if (habitat.Count > 0)
            {
                bodies.Add(new LayoutItem(habitat.Module, habitat.Count));
            }
        }

        if (extraBodies is not null)
        {
            bodies.AddRange(extraBodies.Where(b => b.Count > 0));
        }

        var dockItems = (docks ?? [])
            .Where(d => d.Count > 0)
            .Select(d => new LayoutItem(d.Module, d.Count))
            .ToList();

        return new StationLayout
        {
            Modules = bodies,
            Docks = dockItems,
            Connectors = catalog.GetConnectorModules(),
            PreferredFaction = preferredFaction,
        };
    }
}
