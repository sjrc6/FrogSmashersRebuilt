using System.Text.Json.Serialization;

namespace FrogSmashers.Updater;

[JsonSerializable(typeof(PackageManifest))]
[JsonSerializable(typeof(Installation.Journal))]
[JsonSerializable(typeof(GithubRelease))]
internal partial class UpdaterJson : JsonSerializerContext;
