namespace SphereRabbitMQ.IaC.Cli.Templates.Interfaces;

internal interface ITopologyTemplateCatalog
{
    IReadOnlyList<string> GetTemplateNames();

    string GetTemplateContent(string templateName);
}
