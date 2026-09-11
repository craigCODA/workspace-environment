using System.Diagnostics;
using System.Collections;
using System.Security;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Workspace.Host.Applications;

public sealed class WindowsApplicationCatalog : IApplicationCatalog
{
    private const string AppPathsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
    private readonly IReadOnlyList<IApplicationInventorySource> _sources;

    public WindowsApplicationCatalog(IEnumerable<IApplicationInventorySource>? sources = null)
    {
        _sources = sources?.ToArray() ??
        [
            new AppPathsInventorySource(),
            new StartMenuInventorySource(),
            new AppsFolderInventorySource(),
        ];
    }

    public async Task<IReadOnlyList<ApplicationDescriptor>> ListAsync(CancellationToken cancellationToken)
    {
        var applications = new List<ApplicationDescriptor>();
        foreach (var source in _sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                applications.AddRange(await source.ListAsync(cancellationToken));
            }
            catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException or COMException)
            {
                // An inaccessible source must not hide items discovered by other sources.
            }
        }

        return ApplicationInventory.Merge(applications)
            .OrderBy(application => application.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(application => application.Locator, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<ApplicationDescriptor?> FindByNameAsync(
        string displayName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        var applications = await ListAsync(cancellationToken);
        var resolution = ApplicationResolver.Resolve(displayName, applications);
        return resolution.Status == ApplicationResolutionStatus.Resolved
            ? resolution.Application
            : null;
    }

    private sealed class AppPathsInventorySource : IApplicationInventorySource
    {
        public Task<IReadOnlyList<ApplicationDescriptor>> ListAsync(CancellationToken cancellationToken)
        {
            return Task.Run<IReadOnlyList<ApplicationDescriptor>>(() =>
            {
                var applications = new Dictionary<string, ApplicationDescriptor>(StringComparer.OrdinalIgnoreCase);
                foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                {
                    foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ReadAppPaths(hive, view, applications, cancellationToken);
                    }
                }

                return applications.Values.ToArray();
            }, cancellationToken);
        }
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

    private sealed class StartMenuInventorySource : IApplicationInventorySource
    {
        public Task<IReadOnlyList<ApplicationDescriptor>> ListAsync(CancellationToken cancellationToken)
        {
            var applications = new List<ApplicationDescriptor>();
            foreach (var directory in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            }.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    foreach (var shortcutPath in FindShortcuts(directory, cancellationToken))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var locator = Path.GetFullPath(shortcutPath);
                        applications.Add(new ApplicationDescriptor(
                            CreateStableId(locator),
                            Path.GetFileNameWithoutExtension(locator),
                            ApplicationLaunchKind.Shortcut,
                            locator,
                            Array.Empty<string>()));
                    }
                }
                catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
                {
                    // An inaccessible Start Menu directory contributes no items.
                }
            }

            return Task.FromResult<IReadOnlyList<ApplicationDescriptor>>(applications);
        }
    }

    private static IReadOnlyList<string> FindShortcuts(string rootDirectory, CancellationToken cancellationToken)
    {
        var shortcuts = new List<string>();
        var directories = new Queue<string>();
        directories.Enqueue(rootDirectory);

        while (directories.TryDequeue(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                shortcuts.AddRange(Directory.EnumerateFiles(directory, "*.lnk", SearchOption.TopDirectoryOnly));
            }
            catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
            {
                // One inaccessible directory must not suppress its accessible siblings.
            }

            try
            {
                foreach (var childDirectory in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    directories.Enqueue(childDirectory);
                }
            }
            catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
            {
                // Its children cannot be read, but other queued directories remain available.
            }
        }

        return shortcuts;
    }

    private sealed class AppsFolderInventorySource : IApplicationInventorySource
    {
        public Task<IReadOnlyList<ApplicationDescriptor>> ListAsync(CancellationToken cancellationToken)
        {
            var applications = new List<ApplicationDescriptor>();
            object? shell = null;
            object? folder = null;
            object? items = null;

            try
            {
                var shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType is null)
                {
                    return Task.FromResult<IReadOnlyList<ApplicationDescriptor>>(applications);
                }

                shell = Activator.CreateInstance(shellType);
                if (shell is null)
                {
                    return Task.FromResult<IReadOnlyList<ApplicationDescriptor>>(applications);
                }

                dynamic shellApplication = shell;
                folder = shellApplication.NameSpace("shell:AppsFolder");
                if (folder is null)
                {
                    return Task.FromResult<IReadOnlyList<ApplicationDescriptor>>(applications);
                }

                dynamic appsFolder = folder;
                items = appsFolder.Items();
                foreach (dynamic item in (IEnumerable)items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var appUserModelId = item.Path as string;
                    var displayName = item.Name as string;
                    if (string.IsNullOrWhiteSpace(appUserModelId) || string.IsNullOrWhiteSpace(displayName))
                    {
                        continue;
                    }

                    applications.Add(new ApplicationDescriptor(
                        CreateStableId(appUserModelId),
                        displayName.Trim(),
                        ApplicationLaunchKind.Packaged,
                        CreateAppsFolderLocator(appUserModelId),
                        Array.Empty<string>()));
                }
            }
            catch (Exception exception) when (exception is COMException or UnauthorizedAccessException or SecurityException)
            {
                // AppsFolder is optional and inaccessible shells contribute no items.
            }
            finally
            {
                ReleaseComObject(items);
                ReleaseComObject(folder);
                ReleaseComObject(shell);
            }

            return Task.FromResult<IReadOnlyList<ApplicationDescriptor>>(applications);
        }

        private static void ReleaseComObject(object? value)
        {
            if (value is not null && Marshal.IsComObject(value))
            {
                Marshal.FinalReleaseComObject(value);
            }
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

    public static string CreateAppsFolderLocator(string appUserModelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);
        const string appsFolderPrefix = @"shell:AppsFolder\";
        var normalized = appUserModelId.Trim();
        return normalized.StartsWith(appsFolderPrefix, StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"{appsFolderPrefix}{normalized}";
    }
}
