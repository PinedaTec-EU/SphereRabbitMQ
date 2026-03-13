using System.Reflection;

using SphereRabbitMQ.IaC.Cli.Templates.Interfaces;

namespace SphereRabbitMQ.IaC.Cli.Templates;

internal sealed class TopologyTemplateCatalog : ITopologyTemplateCatalog
{
    private const string ResourcePrefix = "TopologyTemplates/";

    private readonly Assembly _assembly;
    private readonly Dictionary<string, string> _templateResources;

    public TopologyTemplateCatalog()
    {
        _assembly = typeof(TopologyTemplateCatalog).Assembly;
        _templateResources = _assembly
            .GetManifestResourceNames()
            .Where(resourceName => resourceName.Contains(ResourcePrefix, StringComparison.Ordinal))
            .Select(resourceName => new KeyValuePair<string, string>(
                Path.GetFileNameWithoutExtension(resourceName[(resourceName.IndexOf(ResourcePrefix, StringComparison.Ordinal) + ResourcePrefix.Length)..]),
                resourceName))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> GetTemplateNames()
        => _templateResources.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

    public string GetTemplateContent(string templateName)
    {
        if (!_templateResources.TryGetValue(templateName, out var resourceName))
        {
            throw new InvalidOperationException(
                $"Unknown template '{templateName}'. Available templates: {string.Join(", ", GetTemplateNames())}.");
        }

        using var stream = _assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Template resource '{resourceName}' is not available.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
