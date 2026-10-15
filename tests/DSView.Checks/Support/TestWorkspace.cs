// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
namespace DSView.Checks.Support;

// Original shared fixture IO seam for new checks. Existing ad-hoc fixtures are
// migration debt. Each instance owns only its freshly created directory.
internal sealed class TestWorkspace : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("dsview2-checks-");
    public string Root => _directory.FullName;
    private string Resolve(string relative)
    {
        string full = Path.GetFullPath(Path.Combine(Root, relative));
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Fixture path escapes its workspace.", nameof(relative));
        return full;
    }
    public void Write(string relative, string text)
    {
        string path = Resolve(relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text);
    }
    public string Read(string relative) => File.ReadAllText(Resolve(relative));
    public void MakeDirectory(string relative) => Directory.CreateDirectory(Resolve(relative));
    public bool IsDirectory(string relative) => Directory.Exists(Resolve(relative));
    public int TemporaryFiles(string relative) => Directory.GetFiles(Resolve(relative), ".appearance-*.tmp").Length;
    public void Dispose()
    {
        // The immutable DirectoryInfo was created above, never accepted from a caller.
        if (!Path.IsPathFullyQualified(Root) || !_directory.Name.StartsWith("dsview2-checks-", StringComparison.Ordinal))
            throw new IOException("Refusing cleanup outside the owned fixture directory: " + Root);
        try { _directory.Delete(true); }
        catch (IOException ex) { throw new IOException("Fixture cleanup failed: " + Root, ex); }
    }
}
