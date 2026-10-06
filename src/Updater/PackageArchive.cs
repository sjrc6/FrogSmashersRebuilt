using System.Formats.Tar;
using System.IO.Compression;

namespace FrogSmashers.Updater;

internal static class PackageArchive
{
    public static PackageManifest Extract(string archive, string destination, string platform)
    {
        Directory.CreateDirectory(destination);
        string root = platform == "win-x64" ? "FrogSmashersRebuilt-windows/" : "FrogSmashersRebuilt-linux/";
        var extracted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;

        void ExtractFile(string name, long size, Stream data)
        {
            if (!name.StartsWith(root, StringComparison.Ordinal))
                throw new InvalidDataException("Unexpected archive folder: " + name);
            string relative = name[root.Length..];
            string path = PackageManifest.SafePath(destination, relative);
            total += size;
            if (
                size is < 0 or > PackageManifest.MaximumFileSize
                || total > PackageManifest.MaximumPackageSize
                || !extracted.Add(relative)
                || extracted.Count > PackageManifest.MaximumFiles + 1
            )
                throw new InvalidDataException("Duplicate or oversized archive entry: " + name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var output = new FileStream(path, FileMode.CreateNew);
            byte[] buffer = new byte[65536];
            long remaining = size;
            while (remaining > 0)
            {
                int count = data.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (count == 0)
                    throw new InvalidDataException("Incomplete archive entry: " + name);
                output.Write(buffer, 0, count);
                remaining -= count;
            }
            if (data.ReadByte() != -1)
                throw new InvalidDataException("Archive entry exceeds its declared size: " + name);
        }

        if (platform == "win-x64")
        {
            using var zip = ZipFile.OpenRead(archive);
            foreach (var entry in zip.Entries)
            {
                int kind = (entry.ExternalAttributes >> 16) & 0xF000;
                if (kind != 0 && kind != 0x8000)
                    throw new InvalidDataException("Only regular files are allowed in the release archive.");
                using var stream = entry.Open();
                ExtractFile(entry.FullName, entry.Length, stream);
            }
        }
        else
        {
            using var file = File.OpenRead(archive);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);
            while (tar.GetNextEntry() is { } entry)
            {
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                    throw new InvalidDataException("Only regular files are allowed in the release archive.");
                ExtractFile(entry.Name, entry.Length, entry.DataStream ?? Stream.Null);
            }
        }

        var manifest = PackageManifest.Read(destination, platform);
        if (
            !extracted.SetEquals(manifest.Files.Keys.Append(PackageManifest.FileName))
            || !manifest.MatchesFiles(destination)
        )
            throw new InvalidDataException("Downloaded package does not match its installation manifest.");
        if (!OperatingSystem.IsWindows())
            foreach (var (path, file) in manifest.Files)
                File.SetUnixFileMode(
                    PackageManifest.SafePath(destination, path),
                    file.Executable ? (UnixFileMode)0x1ED : (UnixFileMode)0x1A4
                );
        return manifest;
    }
}
