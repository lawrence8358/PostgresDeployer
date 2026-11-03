namespace PostgresDeployer.Core.Tests.Services;

using Microsoft.Extensions.Logging.Abstractions;
using PostgresDeployer.Core.Models;
using PostgresDeployer.Core.Services;

public class DeployOrchestratorTests
{
    private static SchemaChange CascadeChange(string view = "v_base") => new()
    {
        Type = ChangeType.ReplaceView,
        EntityName = view,
        Description = $"Replace view {view}",
        CascadeDropObjects = ["\"v_external\" (view)", "\"rpt\".\"mv_external\" (materialized view)"],
        CautionMessage = $"caution for {view}"
    };

    private static DeployPlan PlanWith(SchemaChange change)
    {
        var plan = new DeployPlan();
        var viewGroup = new DeployGroup { Name = "Views" };
        viewGroup.Statements.Add("CREATE OR REPLACE VIEW \"v_base\" AS SELECT 1 AS a;");
        viewGroup.Changes.Add(change);
        plan.Groups.Add(viewGroup);
        plan.Cautions.Add(change);
        return plan;
    }

    // ═══ ApplyCascadeDropPolicy ═══

    [Fact]
    public void ApplyCascadeDropPolicy_NotAllowed_MovesChangeToBlockers()
    {
        var change = CascadeChange();
        var plan = PlanWith(change);

        DeployOrchestrator.ApplyCascadeDropPolicy(plan, new DeployOptions { AllowCascadeDrop = false });

        Assert.True(plan.IsBlocked);
        Assert.Same(change, Assert.Single(plan.Blockers));
        Assert.Empty(plan.Cautions);
        Assert.NotNull(change.BlockerMessage);
        Assert.Contains("v_base", change.BlockerMessage);
        Assert.Contains("v_external", change.BlockerMessage);
        Assert.Contains("mv_external", change.BlockerMessage);
        Assert.Contains("--allow-cascade-drop", change.BlockerMessage);
        Assert.Contains("allowCascadeDrop", change.BlockerMessage);
    }

    [Fact]
    public void ApplyCascadeDropPolicy_Allowed_KeepsCaution()
    {
        var change = CascadeChange();
        var plan = PlanWith(change);

        DeployOrchestrator.ApplyCascadeDropPolicy(plan, new DeployOptions { AllowCascadeDrop = true });

        Assert.False(plan.IsBlocked);
        Assert.Same(change, Assert.Single(plan.Cautions));
        Assert.Null(change.BlockerMessage);
    }

    [Fact]
    public void ApplyCascadeDropPolicy_ChangeOnlyInGroup_StillBlocked()
    {
        // Pre-Drop 的變更即使沒先放進 Cautions，只要在群組的 Changes 中也要能被擋下
        var change = CascadeChange();
        var plan = PlanWith(change);
        plan.Cautions.Clear();

        DeployOrchestrator.ApplyCascadeDropPolicy(plan, new DeployOptions());

        Assert.Same(change, Assert.Single(plan.Blockers));
    }

    [Fact]
    public void ApplyCascadeDropPolicy_NoCascadeObjects_LeavesPlanUnchanged()
    {
        var change = new SchemaChange
        {
            Type = ChangeType.DropColumn,
            EntityName = "t",
            ColumnName = "c",
            CautionMessage = "drop column"
        };
        var plan = PlanWith(change);

        DeployOrchestrator.ApplyCascadeDropPolicy(plan, new DeployOptions());

        Assert.False(plan.IsBlocked);
        Assert.Same(change, Assert.Single(plan.Cautions));
    }

    // ═══ ExecuteAsync ═══

    private static DeploySettings UnreachableDbSettings(bool allowCascadeDrop) => new()
    {
        // 無法連線的設定：若 ExecuteAsync 沒有在連線前擋下，群組會開始執行並回報進度
        Connection = new ConnectionSettings
        {
            Host = "127.0.0.1",
            Port = 1,
            Database = "pgd_never_exists",
            Username = "nobody",
            Password = "x"
        },
        Options = new DeployOptions { AllowCascadeDrop = allowCascadeDrop }
    };

    [Fact]
    public async Task ExecuteAsync_BlockedPlan_ThrowsBeforeConnecting()
    {
        var plan = PlanWith(CascadeChange());
        DeployOrchestrator.ApplyCascadeDropPolicy(plan, new DeployOptions());
        var progressMessages = new List<string>();
        var orchestrator = new DeployOrchestrator(NullLogger<DeployOrchestrator>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestrator.ExecuteAsync(
                UnreachableDbSettings(allowCascadeDrop: false),
                plan,
                new SyncProgress(progressMessages)));

        Assert.Contains("v_external", ex.Message);
        Assert.Empty(progressMessages); // 沒有任何群組開始執行
    }

    [Fact]
    public async Task ExecuteAsync_BlockedPlanButAllowedAtExecution_DoesNotThrowBlockedError()
    {
        var plan = new DeployPlan(); // 無群組：放行後也不需要連線
        plan.Blockers.Add(CascadeChange());
        var orchestrator = new DeployOrchestrator(NullLogger<DeployOrchestrator>.Instance);

        var results = await orchestrator.ExecuteAsync(UnreachableDbSettings(allowCascadeDrop: true), plan);

        Assert.Empty(results);
    }

    private sealed class SyncProgress(List<string> messages) : IProgress<string>
    {
        public void Report(string value) => messages.Add(value);
    }
}
