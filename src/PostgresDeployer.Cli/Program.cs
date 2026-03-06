using System.CommandLine;
using PostgresDeployer.Cli.Commands;

// ═══ Shared options ═══
var configOption = new Option<string?>("--config", "-c") { Description = "Path to JSON settings file" };
var hostOption = new Option<string?>("--host", "-H") { Description = "PostgreSQL host address" };
var portOption = new Option<int?>("--port", "-P") { Description = "PostgreSQL port" };
var databaseOption = new Option<string?>("--database", "-d") { Description = "Database name" };
var usernameOption = new Option<string?>("--username", "-u") { Description = "Username" };
var passwordOption = new Option<string?>("--password", "-p") { Description = "Password" };

// ═══ Path options ═══
var schemaOption = new Option<string?>("--schema") { Description = "Schema DDL directory path (Tables/Views/Functions/Sequences/Procedures)" };
var initDataOption = new Option<string?>("--init-data") { Description = "Seed data directory path (INSERT / Seed Data)" };
var extensionsOption = new Option<string?>("--extensions") { Description = "PostgreSQL extensions (comma-separated)" };

// ═══ deploy command ═══
var deployCommand = new Command("deploy", "Run database deployment (detect differences and apply changes)");
deployCommand.Add(configOption);
deployCommand.Add(hostOption);
deployCommand.Add(portOption);
deployCommand.Add(databaseOption);
deployCommand.Add(usernameOption);
deployCommand.Add(passwordOption);
deployCommand.Add(schemaOption);
deployCommand.Add(initDataOption);
deployCommand.Add(extensionsOption);

var dryRunOption = new Option<bool>("--dry-run") { Description = "Preview changes only, do not execute" };
var yesOption = new Option<bool>("--yes") { Description = "Skip confirmation prompt and execute immediately" };
var onlyOption = new Option<string>("--only") { Description = "Deploy only the specified type (tables|views|seeds|all)", DefaultValueFactory = _ => "all" };
var logFileOption = new Option<string?>("--log-file") { Description = "Log output file path" };
var stopOnErrorOption = new Option<bool>("--stop-on-error") { Description = "Stop deployment if any group fails (default: true)", DefaultValueFactory = _ => true };

deployCommand.Add(dryRunOption);
deployCommand.Add(yesOption);
deployCommand.Add(onlyOption);
deployCommand.Add(logFileOption);
deployCommand.Add(stopOnErrorOption);

deployCommand.SetAction(async parseResult =>
{
    return await DeployCommand.HandleAsync(
        parseResult.GetValue(configOption),
        parseResult.GetValue(hostOption),
        parseResult.GetValue(portOption),
        parseResult.GetValue(databaseOption),
        parseResult.GetValue(usernameOption),
        parseResult.GetValue(passwordOption),
        parseResult.GetValue(dryRunOption),
        parseResult.GetValue(yesOption),
        parseResult.GetValue(onlyOption)!,
        parseResult.GetValue(stopOnErrorOption),
        parseResult.GetValue(schemaOption),
        parseResult.GetValue(initDataOption),
        parseResult.GetValue(extensionsOption),
        parseResult.GetValue(logFileOption));
});

// ═══ diff command ═══
var diffCommand = new Command("diff", "Analyze schema differences and output a report (no changes applied)");
diffCommand.Add(configOption);
diffCommand.Add(hostOption);
diffCommand.Add(portOption);
diffCommand.Add(databaseOption);
diffCommand.Add(usernameOption);
diffCommand.Add(passwordOption);
diffCommand.Add(schemaOption);
diffCommand.Add(initDataOption);
diffCommand.Add(extensionsOption);

var diffOutputOption = new Option<string?>("--output", "-o") { Description = "Diff report output file path" };
diffCommand.Add(diffOutputOption);

diffCommand.SetAction(async parseResult =>
{
    return await DiffCommand.HandleAsync(
        parseResult.GetValue(configOption),
        parseResult.GetValue(hostOption),
        parseResult.GetValue(portOption),
        parseResult.GetValue(databaseOption),
        parseResult.GetValue(usernameOption),
        parseResult.GetValue(passwordOption),
        parseResult.GetValue(schemaOption),
        parseResult.GetValue(initDataOption),
        parseResult.GetValue(extensionsOption),
        parseResult.GetValue(diffOutputOption));
});

// ═══ test-connection command ═══
var testConnCommand = new Command("test-connection", "Test database connection");
testConnCommand.Add(configOption);
testConnCommand.Add(hostOption);
testConnCommand.Add(portOption);
testConnCommand.Add(databaseOption);
testConnCommand.Add(usernameOption);
testConnCommand.Add(passwordOption);

testConnCommand.SetAction(async parseResult =>
{
    return await TestConnectionCommand.HandleAsync(
        parseResult.GetValue(configOption),
        parseResult.GetValue(hostOption),
        parseResult.GetValue(portOption),
        parseResult.GetValue(databaseOption),
        parseResult.GetValue(usernameOption),
        parseResult.GetValue(passwordOption));
});

// ═══ init command ═══
var initCommand = new Command("init", "Generate a sample settings file");
var initOutputOption = new Option<string>("--output", "-o") { Description = "Output settings file path", DefaultValueFactory = _ => "PostgresDeployer.json" };
initCommand.Add(initOutputOption);

initCommand.SetAction(async parseResult =>
{
    return await InitCommand.HandleAsync(
        parseResult.GetValue(initOutputOption)!);
});

// ═══ Root command ═══
var rootCommand = new RootCommand("PostgresDeployer - General-purpose PostgreSQL schema deployment tool");
rootCommand.Add(deployCommand);
rootCommand.Add(diffCommand);
rootCommand.Add(testConnCommand);
rootCommand.Add(initCommand);

return await rootCommand.Parse(args).InvokeAsync(new InvocationConfiguration(), CancellationToken.None);
