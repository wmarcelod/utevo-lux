using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace UtevoLux.Services;

/// <summary>
/// Run-at-Windows-startup via the per-user HKCU Run key. Value name "UtevoLux",
/// value data <c>"exe" --startup</c>. Per-user (HKCU) needs no elevation.
/// </summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "UtevoLux";
    public const string StartupArg = "--startup";

    private static string ExecutablePath =>
        Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";

    private static string CommandLine => $"\"{ExecutablePath}\" {StartupArg}";

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled)
                key.SetValue(ValueName, CommandLine, RegistryValueKind.String);
            else if (key.GetValue(ValueName) is not null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
            // Registry write blocked by policy: silently no-op (feature is best-effort).
        }
    }
}
