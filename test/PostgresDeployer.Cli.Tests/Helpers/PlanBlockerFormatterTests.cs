namespace PostgresDeployer.Cli.Tests.Helpers;

using PostgresDeployer.Cli.Helpers;
using PostgresDeployer.Core.Models;

public class PlanBlockerFormatterTests
{
    private static DeployPlan BlockedPlan()
    {
        var plan = new DeployPlan();
        var group = new DeployGroup { Name = "Views" };
        group.Statements.Add("CREATE OR REPLACE VIEW \"v\" AS SELECT 1 AS a;");
        plan.Groups.Add(group);
        plan.Blockers.Add(new SchemaChange { EntityName = "v", BlockerMessage = "blocked v" });
        plan.Blockers.Add(new SchemaChange { EntityName = "w", BlockerMessage = "blocked w" });
        return plan;
    }

    [Fact]
    public void FormatBlockedSection_NotBlocked_ReturnsEmpty()
    {
        Assert.Empty(PlanBlockerFormatter.FormatBlockedSection(new DeployPlan()));
    }

    [Fact]
    public void FormatBlockedSection_Blocked_ListsHeaderAndEachBlocker()
    {
        var lines = PlanBlockerFormatter.FormatBlockedSection(BlockedPlan(), indent: "  ");

        Assert.Equal(
            [
                "  [BLOCKED] (2 item(s)) — deployment will not run",
                "    [X] blocked v",
                "    [X] blocked w",
            ],
            lines);
    }

    [Fact]
    public void FormatTotalLine_IncludesBlockerCountOnlyWhenBlocked()
    {
        Assert.Equal("Total: 0 statement(s), 0 caution(s)", PlanBlockerFormatter.FormatTotalLine(new DeployPlan()));
        Assert.Equal(
            "  Total: 1 statement(s), 0 caution(s), 2 blocker(s)",
            PlanBlockerFormatter.FormatTotalLine(BlockedPlan(), indent: "  "));
    }
}
