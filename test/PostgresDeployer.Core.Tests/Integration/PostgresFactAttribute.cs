namespace PostgresDeployer.Core.Tests.Integration;

/// <summary>
/// 需要實際 PostgreSQL 的整合測試。
/// 未設定環境變數 <c>PGD_TEST_CONNECTION</c>（伺服器連線字串，不需 Database）時自動略過。
/// 例：<c>PGD_TEST_CONNECTION="Host=localhost;Port=55432;Username=postgres;Password=pgdtest"</c>
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "PGD_TEST_CONNECTION";

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariable)))
            Skip = $"Set {EnvironmentVariable} to run PostgreSQL integration tests.";
    }
}
