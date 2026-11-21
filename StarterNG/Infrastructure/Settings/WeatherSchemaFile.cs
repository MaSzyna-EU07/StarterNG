using System;
using System.Collections.Generic;
using System.Text;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Settings;

namespace StarterNG.Infrastructure.Settings;

/// <summary>
/// Reads weather-schema.txt from the simulator's folder (see <see cref="WeatherSchema"/>). A simulator built
/// before the file, a file that is not there or cannot be read, all mean no keys: the settings then show
/// what they showed before, and the simulator needs nothing from the starter to run as it always did.
/// </summary>
public static class WeatherSchemaFile
{
    public static IReadOnlyList<SimulatorKey> Read(IFileSystem files, string path, IDiagnosticsLog log)
    {
        try
        {
            if (!files.FileExists(path))
                return Array.Empty<SimulatorKey>();

            var keys = WeatherSchema.Parse(files.ReadAllText(path, new UTF8Encoding(false)));
            if (keys.Count == 0)
                log.Log($"{path}: no weather settings in it that this starter knows");
            return keys;
        }
        catch (Exception ex)
        {
            log.Log("reading " + WeatherSchema.FileName, ex);
            return Array.Empty<SimulatorKey>();
        }
    }
}
