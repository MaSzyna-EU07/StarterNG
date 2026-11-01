using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StarterNG.Application.Abstractions;
using StarterNG.Infrastructure.Settings;

namespace StarterNG.Infrastructure;

/// <summary>
/// Hands a Windows build started through Wine the settings the starter writes.
/// eu07.exe reads eu07.ini and the input files from %APPDATA%\MaSzyna, which Wine
/// keeps inside its prefix whatever APPDATA says - so that folder is made a link
/// to the starter's own, and both see one set of files.
/// </summary>
public sealed class WineConfigLink
{
    private const string VendorFolder = "MaSzyna";
    private const string BackupSuffix = ".before-starter";

    private readonly IFileSystem _files;
    private readonly IEnvironment _environment;
    private readonly SettingsPaths _settingsPaths;
    private readonly IDiagnosticsLog _log;

    public WineConfigLink(IFileSystem files, IEnvironment environment, SettingsPaths settingsPaths,
                          IDiagnosticsLog log)
    {
        _files = files;
        _environment = environment;
        _settingsPaths = settingsPaths;
        _log = log;
    }

    /// <summary>
    /// Makes the prefix's MaSzyna folder a link to the folder the starter saves eu07.ini in.
    /// A folder already there is kept beside it under another name, its files the
    /// starter's folder lacks copied over; a link of the player's own to elsewhere
    /// is left alone. Failing half way, it puts all that back, so the next attempt
    /// starts from the same place. Whatever it does, or why not, goes to the log.
    /// </summary>
    public void Ensure()
    {
        try
        {
            if (RoamingDirectory() is not { } roaming)
            {
                _log.Log("Wine: no prefix with a user profile yet; eu07.exe reads its own eu07.ini this time");
                return;
            }

            string link = Path.Combine(roaming, VendorFolder);
            string target = Path.GetFullPath(_settingsPaths.UserConfigDirectory());
            if (_files.LinkTarget(link) is { } current)
            {
                if (!SamePath(Path.GetFullPath(current, roaming), target))
                    _log.Log($"Wine: {link} links to {current}, not {target}; left as it is");
                return;
            }

            if (_files.FileExists(link))
            {
                _log.Log($"Wine: {link} is a file; settings not linked");
                return;
            }

            _files.CreateDirectory(target);
            string? backup = null;
            var copied = new List<string>();
            try
            {
                if (_files.DirectoryExists(link))
                {
                    string moved = FreeName(link + BackupSuffix);
                    _files.MoveDirectory(link, moved);
                    backup = moved;
                    _log.Log($"Wine: {link} moved to {backup}");
                    CopyMissing(backup, target, copied);
                }

                _files.CreateDirectoryLink(link, target);
                _log.Log($"Wine: {link} now links to {target}");
            }
            catch when (backup is not null)
            {
                PutBack(backup, link, copied);
                throw;
            }
        }
        catch (Exception ex)
        {
            _log.Log("Wine: linking the settings", ex);
        }
    }

    private void PutBack(string backup, string link, IEnumerable<string> copied)
    {
        try
        {
            foreach (string copy in copied)
                _files.DeleteFile(copy);
            _files.MoveDirectory(backup, link);
            _log.Log($"Wine: {link} put back as it was");
        }
        catch (Exception ex)
        {
            _log.Log($"Wine: {link} could not be put back, its files stays at {backup}", ex);
        }
    }

    /// <summary>%APPDATA% inside the prefix the wine command uses, or null before one is made.</summary>
    private string? RoamingDirectory()
    {
        string? prefix = _environment.GetVariable("WINEPREFIX");
        if (string.IsNullOrEmpty(prefix))
        {
            string? home = _environment.GetVariable("HOME");
            if (string.IsNullOrEmpty(home))
                return null;
            prefix = Path.Combine(home, ".wine");
        }

        if (UserProfile(Path.Combine(prefix, "drive_c", "users")) is not { } profile)
            return null;

        // Wine 5 and later lay the profile out as Vista does; older ones as XP did.
        string roaming = Path.Combine(profile, "AppData", "Roaming");
        if (_files.DirectoryExists(roaming))
            return roaming;
        string applicationData = Path.Combine(profile, "Application Data");
        return _files.DirectoryExists(applicationData) ? applicationData : null;
    }

    /// <summary>The profile named after the user, as Wine names it; or the only one there is.</summary>
    private string? UserProfile(string users)
    {
        if (!_files.DirectoryExists(users))
            return null;

        foreach (string variable in new[] { "USER", "LOGNAME" })
        {
            string? name = _environment.GetVariable(variable);
            if (!string.IsNullOrEmpty(name) && _files.DirectoryExists(Path.Combine(users, name)))
                return Path.Combine(users, name);
        }

        var profiles = _files.GetDirectories(users)
            .Where(path => !string.Equals(Path.GetFileName(path), "Public", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return profiles.Count == 1 ? profiles[0] : null;
    }

    // What eu07.exe kept in the prefix alone - eu07_input-uart.ini set up under
    // Wine, say - so it still finds it; the starter's copy wins where both have one.
    private void CopyMissing(string from, string to, List<string> copied)
    {
        foreach (string file in _files.GetFiles(from))
        {
            string copy = Path.Combine(to, Path.GetFileName(file));
            if (_files.FileExists(copy))
                continue;
            _files.CopyFile(file, copy, overwrite: false);
            copied.Add(copy);
            _log.Log($"Wine: {Path.GetFileName(file)} copied to {to}");
        }
    }

    private string FreeName(string path)
    {
        string name = path;
        for (int n = 2; _files.DirectoryExists(name) || _files.FileExists(name) || _files.LinkTarget(name) != null; n++)
            name = $"{path}-{n}";
        return name;
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(a.TrimEnd('/', '\\'), b.TrimEnd('/', '\\'), StringComparison.Ordinal);
}
