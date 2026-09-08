using System.Diagnostics;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Workspace.Host.Applications;

public sealed class WindowsApplicationCatalog : IApplicationCatalog
{
    private const string AppPathsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";

    public Task<IReadOnlyList<ApplicationDescriptor>> ListAsync(CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<ApplicationDescriptor>>(
            () => Discover(cancellationToken),
            cancellationToken);
    }

    public async Task<ApplicationDescriptor?> FindByNameAsync(
        string displayName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        var normalized = displayName.Trim();
        var applications = await ListAsync(cancellationToken);

        return applications.FirstOrDefault(app =>
            string.Equals(app.DisplayName, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<ApplicationDescriptor> Discover(CancellationToken cancellationToken)
    {
        var byExecutable = new Dictionary<string, ApplicationDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReadAppPaths(hive, view, byExecutable, cancellationToken);
            }
        }

        return byExecutable.Values
            .OrderBy(application => application.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(application => application.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void ReadAppPaths(
        RegistryHive hive,
        RegistryView view,
        IDictionary<string, ApplicationDescriptor> applications,
        CancellationToken cancellationToken)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var appPaths = baseKey.OpenSubKey(AppPathsKey);
            if (appPaths is null)
            {
                return;
            }

            foreach (var subKeyName in appPaths.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var appKey = appPaths.OpenSubKey(subKeyName);
                var rawPath = appKey?.GetValue(null) as string;
                if (!TryNormalizeExecutablePath(rawPath, out var executablePath))
                {
                    continue;
                }

                var displayName = GetDisplayName(executablePath);
                applications[executablePath] = new ApplicationDescriptor(
                    CreateStableId(executablePath),
                    displayName,
                    executablePath,
                    null);
            }
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            // An inaccessible registry view is not fatal to the inventory. Other views are still usable.
        }
    }

    private static bool TryNormalizeExecutablePath(string? rawPath, out string executablePath)
    {
        executablePath = string.Empty;
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return false;
        }

        var expanded = Environment.ExpandEnvironmentVariables(rawPath.Trim().Trim('"'));
        if (!Path.IsPathFullyQualified(expanded) || !File.Exists(expanded))
        {
            return false;
        }

        executablePath = Path.GetFullPath(expanded);
        return string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDisplayName(string executablePath)
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(executablePath).FileDescription;
            if (!string.IsNullOrWhiteSpace(description))
            {
                return description.Trim();
            }
        }
        catch (FileNotFoundException)
        {
        }

        return Path.GetFileNameWithoutExtension(executablePath);
    }

    public static string CreateStableId(string executablePath)
    {
        var canonical = executablePath.Replace('/', '\\').ToUpperInvariant();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return $"pc.application:{Convert.ToHexString(digest[..12]).ToLowerInvariant()}";
    }
}
