using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.CommandLine.Invocation;
using System.Threading.Tasks;
using Guide.Cli.Commands;

namespace Guide.Cli
{
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            RootCommand rootCommand = new RootCommand("GUIDE Assistant Engineering Platform CLI");

            Option<string> pathOption = new Option<string>("--path", new[] { "-p" })
            {
                Description = "Path to the repository root",
                DefaultValueFactory = _ => "."
            };

            Command initCommand = new Command("init", "Initialize the repository with databases and IA rules templates");
            initCommand.Add(pathOption);
            initCommand.SetAction(async (parseResult, cancellationToken) =>
            {
                string path = parseResult.GetValue(pathOption) ?? ".";
                return await InitCommand.InvokeAsync(path);
            });
            rootCommand.Add(initCommand);

            Option<bool> runTestsOption = new Option<bool>("--run-tests", Array.Empty<string>())
            {
                Description = "Run tests as part of the validation pipeline",
                DefaultValueFactory = _ => false
            };
            Option<bool> autoHealOption = new Option<bool>("--auto-heal", Array.Empty<string>())
            {
                Description = "Enables the automated AI-powered healing loop if static validation fails.",
                DefaultValueFactory = _ => false
            };
            Option<bool> quietOption = new Option<bool>("--quiet", Array.Empty<string>())
            {
                Description = "Suppresses all decorative, verbose, and ASCII art logs for cleaner machine/agent execution."
            };
            Command validateCommand = new Command("validate", "Run all project validators and transition FSM");
            validateCommand.Add(pathOption);
            validateCommand.Add(runTestsOption);
            validateCommand.Add(autoHealOption);
            validateCommand.Add(quietOption);
            validateCommand.SetAction(async (parseResult, cancellationToken) =>
            {
                string path = parseResult.GetValue(pathOption) ?? ".";
                bool runTests = parseResult.GetValue(runTestsOption);
                bool autoHeal = parseResult.GetValue(autoHealOption);
                bool quiet = parseResult.GetValue(quietOption);
                return await ValidateCommand.InvokeAsync(path, runTests, autoHeal, quiet);
            });
            rootCommand.Add(validateCommand);

            Command hookCommand = new Command("hook", "Install local git hooks (e.g. pre-push)");
            hookCommand.Add(pathOption);
            hookCommand.SetAction(async (parseResult, cancellationToken) =>
            {
                string path = parseResult.GetValue(pathOption) ?? ".";
                return await HookCommand.InvokeAsync(path);
            });
            rootCommand.Add(hookCommand);

            Command indexCommand = new Command("index", "Scan the codebase, extract semantic graph and persist snapshot");
            indexCommand.Add(pathOption);
            indexCommand.SetAction(async (parseResult, cancellationToken) =>
            {
                string path = parseResult.GetValue(pathOption) ?? ".";
                return await IndexCommand.InvokeAsync(path);
            });
            rootCommand.Add(indexCommand);

            Option<string> anchorOption = new Option<string>("--anchor", new[] { "-a" })
            {
                Description = "The anchor entity name",
                DefaultValueFactory = _ => ""
            };

            Option<int> depthOption = new Option<int>("--depth", new[] { "-d" })
            {
                Description = "BFS traversal depth",
                DefaultValueFactory = _ => 2
            };

            Command queryContextCommand = new Command("query-context", "Query semantic graph for context relative to an anchor");
            queryContextCommand.Add(pathOption);
            queryContextCommand.Add(anchorOption);
            queryContextCommand.Add(depthOption);
            queryContextCommand.SetAction(async (parseResult, cancellationToken) =>
            {
                string path = parseResult.GetValue(pathOption) ?? ".";
                string anchor = parseResult.GetValue(anchorOption) ?? "";
                int depth = parseResult.GetValue(depthOption);
                return await QueryContextCommand.InvokeAsync(path, anchor, depth);
            });
            rootCommand.Add(queryContextCommand);

            Argument<string> queryArgument = new Argument<string>("query") { Description = "The search query string" };
            Command searchCommand = new Command("search", "Search the engineering knowledge rules database");
            searchCommand.Add(queryArgument);
            searchCommand.Add(pathOption);
            searchCommand.SetAction(async (parseResult, cancellationToken) =>
            {
                string path = parseResult.GetValue(pathOption) ?? ".";
                string query = parseResult.GetValue(queryArgument) ?? "";
                return await SearchCommand.InvokeAsync(path, query);
            });
            rootCommand.Add(searchCommand);

            Argument<string> anchorArgument = new Argument<string>("anchor") { Description = "The anchor type name" };
            Command whyCommand = new Command("why", "Explain why an entity exists and trace its relationship chain");
            whyCommand.Add(anchorArgument);
            whyCommand.Add(pathOption);
            whyCommand.SetAction(async (parseResult, cancellationToken) =>
            {
                string path = parseResult.GetValue(pathOption) ?? ".";
                string anchor = parseResult.GetValue(anchorArgument) ?? "";
                return await WhyCommand.InvokeAsync(path, anchor);
            });
            rootCommand.Add(whyCommand);

            ParseResult parseResult = CommandLineParser.Parse(rootCommand, args, null);
            return await parseResult.InvokeAsync(null);
        }
    }
}
