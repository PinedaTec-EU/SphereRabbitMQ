using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

using SphereRabbitMQ.IaC.Cli.Commands.Interfaces;

namespace SphereRabbitMQ.IaC.Cli.Commands;

[ExcludeFromCodeCoverage]
internal sealed class CommandOutputWriter : ICommandOutputWriter
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public void WriteText(string content)
    {
        Console.WriteLine(content);
    }

    public void WriteJson<T>(T content)
    {
        Console.WriteLine(JsonSerializer.Serialize(content, JsonSerializerOptions));
    }
}
