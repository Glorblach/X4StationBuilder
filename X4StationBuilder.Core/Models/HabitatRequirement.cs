namespace X4StationBuilder.Core.Models;

/// <summary>
/// A habitat selection for the workforce: a habitat module and how many of it are required to house
/// the station's workers.
/// </summary>
public sealed record HabitatRequirement(StationModule Module, int Count)
{
    /// <summary>Workers a single habitat module can house.</summary>
    public int CapacityPerModule => Module.WorkforceCapacity;

    /// <summary>Total workers housed by all habitats in this requirement.</summary>
    public int HousedWorkers => Count * Module.WorkforceCapacity;
}
