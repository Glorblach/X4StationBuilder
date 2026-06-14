namespace X4StationBuilder.Core.Models;

/// <summary>
/// A user-specified docking selection: a dock (or pier) module and how many of it to include in the
/// station. Docks are not produced by recipes — they are added directly to the assembled module list
/// consumed by the layout algorithm (Step 09) and XML export (Step 10).
/// </summary>
public sealed record DockRequest(StationModule Module, int Count);
