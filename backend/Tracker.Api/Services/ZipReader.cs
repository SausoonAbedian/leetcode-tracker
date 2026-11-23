using System.IO.Compression;
using System.Text;

namespace Tracker.Api.Services;

public class UploadException(string message) : Exception(message);

/// <summary>Reads an uploaded zip into memory as text files, with size limits and path sanitising (no zip-slip: nothing is written to disk).</summary>
public static class ZipReader
{
    static readonly string[] SkipDirs = ["bin", "obj", ".git", ".idea", ".vs", "node_modules", "TestResults"];

    public static Dictionary<string, string> ReadTextFiles(Stream zip, long maxTotalBytes = 200L * 1024 * 1024, int maxEntries = 20000)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        long total = 0;
        ZipArchive archive;
        try { archive = new ZipArchive(zip, ZipArchiveMode.Read); }
        catch (InvalidDataException) { throw new UploadException("The upload is not a valid zip file."); }
        using (archive)
        {
            if (archive.Entries.Count > maxEntries) throw new UploadException($"Zip has too many entries (> {maxEntries}).");
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith('/') || entry.Length == 0 && entry.Name == "") continue;
                var path = Normalize(entry.FullName);
                if (path is null) continue;
                if (path.Split('/').Any(seg => SkipDirs.Contains(seg, StringComparer.OrdinalIgnoreCase))) continue;
                if (!IsText(path)) continue;
                total += entry.Length;
                if (total > maxTotalBytes) throw new UploadException("Zip content is too large.");
                using var s = entry.Open();
                using var r = new StreamReader(s, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                files[path] = r.ReadToEnd().Replace("\r\n", "\n");
            }
        }
        return files;
    }

    /// <summary>Returns a clean relative path with forward slashes, or null if it is unsafe.</summary>
    public static string? Normalize(string name)
    {
        var p = name.Replace('\\', '/').TrimStart('/');
        var parts = p.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(x => x == ".." || x == "." || x.Contains(':'))) return null;
        return string.Join('/', parts);
    }

    static bool IsText(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".cs" or ".csproj" or ".props" or ".targets" or ".md" or ".txt" or ".json" or ".slnx" or ".sln" or ".ps1" or ".xml" or ".editorconfig" or "";
    }
}
