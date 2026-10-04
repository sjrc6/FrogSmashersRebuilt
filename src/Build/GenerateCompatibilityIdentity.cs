using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public sealed class GenerateCompatibilityIdentity : Task
{
    [Required]
    public string Root { get; set; }

    [Required]
    public ITaskItem[] Inputs { get; set; }

    [Required]
    public ITaskItem[] References { get; set; }

    [Required]
    public string Options { get; set; }

    [Output]
    public string Identity { get; set; }

    public override bool Execute()
    {
        string root = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using (var buffer = new MemoryStream())
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true))
        using (var sha = SHA256.Create())
        {
            writer.Write("FrogSmashers.BuildInputs.1");
            writer.Write(Options);
            var inputs = Inputs.Select(item => Path.GetFullPath(item.ItemSpec)).Distinct().ToArray();
            foreach (string path in inputs)
            {
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    Log.LogError("Simulation build inputs must be inside the repository: {0}", path);
                    return false;
                }
            }
            writer.Write(inputs.Length);
            foreach (
                string path in inputs.OrderBy(
                    path => path.Substring(root.Length).Replace('\\', '/'),
                    StringComparer.Ordinal
                )
            )
            {
                writer.Write(path.Substring(root.Length).Replace('\\', '/'));
                writer.Write(File.ReadAllText(path).Replace("\r\n", "\n"));
            }
            writer.Write(References.Length);
            foreach (
                var reference in References.OrderBy(item => Path.GetFileName(item.ItemSpec), StringComparer.Ordinal)
            )
            {
                writer.Write(Path.GetFileName(reference.ItemSpec));
                using (var file = File.OpenRead(reference.ItemSpec))
                    writer.Write(sha.ComputeHash(file));
            }
            writer.Flush();
            Identity = BitConverter.ToString(sha.ComputeHash(buffer.ToArray())).Replace("-", "");
        }
        return true;
    }
}
