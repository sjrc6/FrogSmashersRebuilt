namespace FrogSmashers.Client;

internal readonly record struct ControllerDevice(string? Name, string? Identifier)
{
    public string BindingKey =>
        !string.IsNullOrWhiteSpace(Identifier) ? "id:" + Identifier.Trim().ToLowerInvariant()
        : !string.IsNullOrWhiteSpace(Name) ? "name:" + Name.Trim().ToUpperInvariant()
        : "generic";
}
