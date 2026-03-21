using System.Text.Json;
using SphereRabbitMQ.IaC.Cli.Commands;
using SphereRabbitMQ.IaC.Cli.Commands.Models;
using SphereRabbitMQ.IaC.Domain.Planning;
using SphereRabbitMQ.IaC.Domain.Topology;

namespace SphereRabbitMQ.IaC.Tests.Unit.Cli;

public sealed class CommandOutputRendererTests
{
    [Fact]
    public void RenderPlan_IncludesBlockingDestructiveChanges()
    {
        var result = new PlanCommandResult(
            CreateBroker(),
            new TopologyValidationResult(Array.Empty<TopologyIssue>()),
            new TopologyPlan(
            [
                new TopologyPlanOperation(
                    TopologyPlanOperationKind.DestructiveChange,
                    TopologyResourceKind.Queue,
                    "/virtualHosts/sales/queues/orders",
                    "Queue 'orders' requires delete/recreate because immutable properties changed.",
                    [new TopologyDiff("/virtualHosts/sales/queues/orders", TopologyResourceKind.Queue, "type", "Quorum", "Classic")]),
            ],
            destructiveChanges:
            [
                new DestructiveChangeWarning("/virtualHosts/sales/queues/orders", "Queue 'orders' requires delete/recreate because immutable properties changed."),
            ]));

        var text = CommandOutputRenderer.RenderPlan(result);

        Assert.Contains("Blocking plan operations:", text, StringComparison.Ordinal);
        Assert.Contains("[destructive] /virtualHosts/sales/queues/orders", text, StringComparison.Ordinal);
        Assert.Contains("diff type: desired=Quorum actual=Classic", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderApply_IncludesBlockingUnsupportedChanges()
    {
        var result = new ApplyCommandResult(
            false,
            CreateBroker(),
            new TopologyValidationResult(Array.Empty<TopologyIssue>()),
            new TopologyPlan(
            [
                new TopologyPlanOperation(
                    TopologyPlanOperationKind.UnsupportedChange,
                    TopologyResourceKind.Exchange,
                    "/virtualHosts/sales/exchanges/orders",
                    "Exchange 'orders' requires redeclaration because immutable properties changed.",
                    [new TopologyDiff("/virtualHosts/sales/exchanges/orders", TopologyResourceKind.Exchange, "type", "Topic", "Direct")]),
            ],
            unsupportedChanges:
            [
                new UnsupportedChange("/virtualHosts/sales/exchanges/orders", "Exchange 'orders' requires redeclaration because immutable properties changed."),
            ]));

        var text = CommandOutputRenderer.RenderApply(result);

        Assert.Contains("Apply completed.", text, StringComparison.Ordinal);
        Assert.Contains("Execution plan:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\nPlan:\n", text, StringComparison.Ordinal);
        Assert.Contains("[unsupported] /virtualHosts/sales/exchanges/orders", text, StringComparison.Ordinal);
        Assert.Contains("diff type: desired=Topic actual=Direct", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanCommandResult_SerializesBlockingChanges_ForJsonOutput()
    {
        var result = new PlanCommandResult(
            CreateBroker(),
            new TopologyValidationResult(Array.Empty<TopologyIssue>()),
            new TopologyPlan(
            [
                new TopologyPlanOperation(
                    TopologyPlanOperationKind.DestructiveChange,
                    TopologyResourceKind.Queue,
                    "/virtualHosts/sales/queues/orders",
                    "Queue 'orders' requires delete/recreate because immutable properties changed.",
                    [new TopologyDiff("/virtualHosts/sales/queues/orders", TopologyResourceKind.Queue, "type", "Quorum", "Classic")]),
            ],
            destructiveChanges:
            [
                new DestructiveChangeWarning("/virtualHosts/sales/queues/orders", "Queue 'orders' requires delete/recreate because immutable properties changed."),
            ]));

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"blockingChanges\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"destructive\"", json, StringComparison.Ordinal);
        Assert.Contains("\"resourcePath\":\"/virtualHosts/sales/queues/orders\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderPurge_IncludesQueuesToPurge()
    {
        var result = new PurgeCommandResult(
            false,
            CreateBroker(),
            TopologyValidationResult.Success,
            [
                new PurgeQueueResult("sales", "orders", "/virtualHosts/sales/queues/orders"),
                new PurgeQueueResult("sales", "orders.debug", "/virtualHosts/sales/queues/orders.debug"),
            ]);

        var text = CommandOutputRenderer.RenderPurge(result);

        Assert.Contains("Purge completed.", text, StringComparison.Ordinal);
        Assert.Contains("Queues to purge:", text, StringComparison.Ordinal);
        Assert.Contains("/virtualHosts/sales/queues/orders", text, StringComparison.Ordinal);
        Assert.Contains("/virtualHosts/sales/queues/orders.debug", text, StringComparison.Ordinal);
    }

    private static BrokerResolutionResult CreateBroker()
        => new(
            new BrokerOptions("http://localhost:15672/api/", "guest", "guest", ["sales"]),
            new BrokerOptionValue<string>("http://localhost:15672/api/", BrokerOptionSource.Default),
            new BrokerOptionValue<string>("guest", BrokerOptionSource.Default),
            BrokerOptionSource.Default,
            new BrokerOptionValue<IReadOnlyList<string>>(["sales"], BrokerOptionSource.Derived));
}
