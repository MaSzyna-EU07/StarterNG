using System;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Settings;
using StarterNG.Infrastructure.Vehicles;

namespace StarterNG.Application;

/// <summary>
/// Writes the "vehicle files that are not there" report into the diagnostics
/// log, when the user has asked for it.
/// </summary>
/// <remarks>
/// Was a method on the settings object, which had no business scanning the
/// installation. The setting decides whether it runs; the scan itself belongs to
/// the vehicle side.
/// </remarks>
public sealed class MissingVehicleLog
{
    private readonly SimulatorSettings _settings;
    private readonly MissingAssetScanner _scanner;
    private readonly GameLibrary _library;
    private readonly IDiagnosticsLog _log;

    public MissingVehicleLog(SimulatorSettings settings, MissingAssetScanner scanner, GameLibrary library,
                             IDiagnosticsLog log)
    {
        _settings = settings;
        _scanner = scanner;
        _library = library;
        _log = log;
    }

    public void Dump()
    {
        if (!_settings.LogMissingVehicleFiles)
            return;

        try
        {
            var lines = _scanner.Scan(_library.Vehicles);
            if (lines.Count > 0)
                _log.Log(lines);
        }
        catch (Exception ex)
        {
            _log.Log("Missing vehicle log", ex);
        }
    }
}
