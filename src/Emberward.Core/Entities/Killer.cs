namespace Emberward.Core.Entities;

using Emberward.Core.Entities.Machines;

/// <summary>
/// Who dealt a killing hit: the side's colour (a tank's colour, and so its place in the tank list), and the machine
/// that fired the shot, or null when the tank fired it itself.
/// </summary>
public readonly record struct Killer(int Color, MachineType? Machine = null);
