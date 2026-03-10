namespace SphereRabbitMQ.IaC.Cli.Commands.Interfaces;

internal interface ICommandOutputWriter
{
    void WriteText(string content);

    void WriteJson<T>(T content);
}
