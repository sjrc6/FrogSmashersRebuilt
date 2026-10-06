using System.Text.Json;

namespace FrogSmashers.Updater;

internal sealed class Installation(string directory, string platform)
{
    private const string BackupName = ".update-backup";
    private const string BackupOwner = "FrogSmashersRebuilt update backup";
    private string Backup => Path.Combine(directory, BackupName);

    private sealed record Journal(PackageManifest Previous, PackageManifest Next, string[] Existing);

    public void Apply(string staged, PackageManifest previous, PackageManifest next)
    {
        foreach (string name in previous.Files.Keys.Union(next.Files.Keys).Append(PackageManifest.FileName))
        {
            string path = PackageManifest.SafePath(directory, name);
            if (Directory.Exists(path))
                throw new IOException("A folder occupies a package file path: " + name);
            if (File.Exists(path) && name != PackageManifest.FileName && !previous.Files.ContainsKey(name))
                throw new IOException("Update would overwrite a file that is not owned by the installation: " + name);
        }
        if (Directory.Exists(Backup))
            throw new IOException("An earlier update must be recovered first.");
        Directory.CreateDirectory(Backup);
        File.WriteAllText(Path.Combine(Backup, "owner"), BackupOwner);
        var existing = new List<string>();
        try
        {
            foreach (string name in previous.Files.Keys.Append(PackageManifest.FileName))
            {
                string source = PackageManifest.SafePath(directory, name);
                if (!File.Exists(source))
                    continue;
                string saved = PackageManifest.SafePath(Path.Combine(Backup, "files"), name);
                Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                File.Copy(source, saved);
                existing.Add(name);
            }
            var journal = new Journal(previous, next, existing.ToArray());
            using (var output = new FileStream(Path.Combine(Backup, "journal.tmp"), FileMode.CreateNew))
            {
                JsonSerializer.Serialize(output, journal);
                output.Flush(true);
            }
            File.Move(Path.Combine(Backup, "journal.tmp"), Path.Combine(Backup, "journal.json"));
            foreach (string name in next.Files.Keys.Append(PackageManifest.FileName))
            {
                string target = PackageManifest.SafePath(directory, name);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(PackageManifest.SafePath(staged, name), target, true);
            }
            foreach (string name in previous.Files.Keys.Except(next.Files.Keys))
                File.Delete(PackageManifest.SafePath(directory, name));
            File.WriteAllText(Path.Combine(Backup, "complete"), "complete");
        }
        catch (Exception failure)
        {
            try
            {
                Recover();
            }
            catch (Exception recovery)
            {
                throw new AggregateException(
                    "Update failed and recovery could not finish. Close the game and run the updater again. Backups remain in "
                        + Backup,
                    failure,
                    recovery
                );
            }
            throw new IOException("Update failed; the previous installation was restored. " + failure.Message, failure);
        }
        CleanupBackup();
    }

    public void Recover()
    {
        if (!Directory.Exists(Backup))
            return;
        if (
            new DirectoryInfo(Backup).LinkTarget != null
            || File.ReadAllText(Path.Combine(Backup, "owner")) != BackupOwner
        )
            throw new IOException("An unrelated folder occupies " + Backup + ".");
        string journalPath = Path.Combine(Backup, "journal.json");
        if (!File.Exists(journalPath) || File.Exists(Path.Combine(Backup, "complete")))
        {
            CleanupBackup();
            return;
        }
        var journal =
            JsonSerializer.Deserialize<Journal>(File.ReadAllText(journalPath))
            ?? throw new InvalidDataException("The update recovery journal is unreadable.");
        journal.Previous.Validate(platform);
        journal.Next.Validate(platform);
        var owned = journal
            .Previous.Files.Keys.Union(journal.Next.Files.Keys)
            .Append(PackageManifest.FileName)
            .ToHashSet();
        if (journal.Existing == null || journal.Existing.Any(name => !owned.Contains(name)))
            throw new InvalidDataException("Invalid update recovery file list.");
        foreach (string name in journal.Existing)
            if (!File.Exists(PackageManifest.SafePath(Path.Combine(Backup, "files"), name)))
                throw new IOException("An update backup is missing: " + name);
        foreach (string name in owned.Except(journal.Existing))
            File.Delete(PackageManifest.SafePath(directory, name));
        foreach (string name in journal.Existing)
        {
            string saved = PackageManifest.SafePath(Path.Combine(Backup, "files"), name);
            string target = PackageManifest.SafePath(directory, name);
            if (File.Exists(target) && PackageManifest.Hash(saved) == PackageManifest.Hash(target))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string temporary = Path.Combine(Backup, "restoring-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.Copy(saved, temporary);
                File.Move(temporary, target, true);
            }
            finally
            {
                File.Delete(temporary);
            }
        }
        CleanupBackup();
    }

    private void CleanupBackup()
    {
        string cleanup = Path.Combine(directory, ".update-cleanup-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.Move(Backup, cleanup);
            Directory.Delete(cleanup, true);
        }
        catch (IOException)
        {
            Console.WriteLine("Update backup cleanup could not finish: " + cleanup);
        }
        catch (UnauthorizedAccessException)
        {
            Console.WriteLine("Update backup cleanup could not finish: " + cleanup);
        }
    }
}
