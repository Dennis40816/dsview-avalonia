// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;

namespace DSView.Infrastructure;

// Filesystem seam owner. The path is fixed within local app data, never supplied
// by a capture or decoder. Immutable fields; one UI settings operation at a time.
public sealed class LocalAppearanceStore : IAppearanceStore
{
    private readonly string? _path;
    public LocalAppearanceStore() : this(new(() => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))) { }
    internal LocalAppearanceStore(LocalAppearanceStoreSeams seams)
    {
        string? root = seams.ResolveLocalDataDirectory();
        _path = root != null && Path.IsPathFullyQualified(root) ? Path.Combine(root, "DSView2", "appearance.json") : null;
    }

    public AppearanceLoadResult Load()
    {
        if (_path == null) return AppearanceLoadResult.Invalid;
        try
        {
            using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > 16_384) return AppearanceLoadResult.Invalid;
            using var reader = new StreamReader(file, new UTF8Encoding(false, true));
            return DisplayPreferences.Admit(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        { return new(DisplayPreferences.Default, null); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        { return AppearanceLoadResult.Invalid; }
    }

    public Task<AppearanceSaveResult> SaveAsync(DisplayPreferences preferences) => Task.Run(() => Save(preferences));

    private AppearanceSaveResult Save(DisplayPreferences preferences)
    {
        if (_path == null || !preferences.IsValid) return AppearanceSaveResult.Failed;
        string directory = Path.GetDirectoryName(_path)!;
        string staged = Path.Combine(directory, ".appearance-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(directory);
            using (var file = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(preferences.Serialize());
                file.Write(bytes); file.Flush(true);
            }
            File.Move(staged, _path, true);
            return AppearanceSaveResult.Saved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Failed before publication: report a typed failure and retain the old file.
            try { File.Delete(staged); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            { return new(false, "Preferences could not be saved. A temporary file remains in the local settings folder."); }
            return AppearanceSaveResult.Failed;
        }
    }
}

internal sealed record LocalAppearanceStoreSeams(Func<string?> ResolveLocalDataDirectory);
