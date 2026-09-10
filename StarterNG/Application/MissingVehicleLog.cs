using System;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Settings;
using StarterNG.Infrastructure.Vehicles;

namespace StarterNG.Application;

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
