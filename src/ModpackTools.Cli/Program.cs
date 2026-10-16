using System.Text;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Cli;
using ModpackTools.Core.Data;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Diagnostics;

Console.OutputEncoding = new UTF8Encoding(false);
if (args.Length == 1 && args[0] == "--tool-child") return await ModpackTools.Core.Processes.NativeToolChild.RunAsync();
InvocationContext? context = null;
string? reviewToken = null;
DoctorRepairScope? doctorScope = null;
try
{
    if (args.Length > 0 && args[0] == "--request-path")
    {
        if (args.Length != 2)
            throw new ProtocolException("Use --request-path <file> without additional arguments.");
        var request = RequestReader.Read(args[1]);
        args = request.Arguments;
        context = request.Context;
        reviewToken = request.ReviewToken;
        doctorScope = request.DoctorScope;
    }
}
catch (Exception exception) when (exception is ProtocolException or JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
{
    var invalid = FailureEnvelope.Create("", [],
        Failure.From("Core.InvalidRequest", exception.Message, "InvalidData", null));
    Console.WriteLine(JsonSerializer.Serialize(invalid, WireJsonContext.Default.FailureEnvelope));
    return 2;
}

var command = "";
string[] commandArguments = [];
try
{
    var options = PresentationOptions.Parse(args);
    command = options.Arguments.FirstOrDefault() ?? "";
    commandArguments = options.Arguments.Skip(1).ToArray();
    if (doctorScope is not null && (!command.Equals("doctor", StringComparison.OrdinalIgnoreCase) ||
        !commandArguments.Contains("--fix", StringComparer.OrdinalIgnoreCase) || HelpCommand.Applies(command, commandArguments)))
        throw new DomainException("Doctor.InvalidScope", "Doctor repair scope requires doctor --fix", "submit a doctor repair phase without --help", "InvalidArgument");
    var reviewedRemoval = command.Equals("content", StringComparison.OrdinalIgnoreCase) &&
        commandArguments.FirstOrDefault()?.Equals("remove", StringComparison.OrdinalIgnoreCase) == true;
    var reviewedDoctor = command.Equals("doctor", StringComparison.OrdinalIgnoreCase) && doctorScope == DoctorRepairScope.Project;
    if (reviewToken is not null && ((!reviewedRemoval && !reviewedDoctor) ||
        !commandArguments.Contains("--yes", StringComparer.OrdinalIgnoreCase) ||
        commandArguments.Contains("--dry-run", StringComparer.OrdinalIgnoreCase) || HelpCommand.Applies(command, commandArguments)))
        throw new DomainException("Transaction.InvalidReview", "A review token requires content remove --yes or a project doctor repair --yes",
            "submit the reviewed operation without --dry-run or --help", "InvalidArgument");
    if (HelpCommand.Applies(command, commandArguments))
    {
        HelpCommand.Run(command, commandArguments);
        return 0;
    }
    if (new[] { "build", "diff", "doctor" }.Contains(command, StringComparer.OrdinalIgnoreCase) && !commandArguments.Contains("--help", StringComparer.OrdinalIgnoreCase))
    {
        var configurationDirectory = Environment.GetEnvironmentVariable("MODPACKTOOLS_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configurationDirectory)) configurationDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools");
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, signal) => { signal.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            if (command.Equals("build", StringComparison.OrdinalIgnoreCase)) await BuildCommand.RunAsync(command, commandArguments, options.Project, context is null ? Environment.GetEnvironmentVariable("MODPACKTOOLS_PROJECT") : context.ActiveProject, configurationDirectory, cancellation.Token, !options.NoHuman);
            else if (command.Equals("doctor", StringComparison.OrdinalIgnoreCase)) await DoctorCommand.RunAsync(command, commandArguments, options.Project, context is null ? Environment.GetEnvironmentVariable("MODPACKTOOLS_PROJECT") : context.ActiveProject, configurationDirectory, cancellation.Token, doctorScope ?? DoctorRepairScope.All, !options.NoHuman, reviewToken);
            else await DiffCommand.RunAsync(command, commandArguments, options.Project, context is null ? Environment.GetEnvironmentVariable("MODPACKTOOLS_PROJECT") : context.ActiveProject, configurationDirectory, cancellation.Token, !options.NoHuman);
        }
        finally { Console.CancelKeyPress -= cancel; }
        return 0;
    }
    if (command.Equals("mod", StringComparison.OrdinalIgnoreCase) && !commandArguments.Contains("--help", StringComparer.OrdinalIgnoreCase))
    {
        var configurationDirectory = Environment.GetEnvironmentVariable("MODPACKTOOLS_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configurationDirectory)) configurationDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools");
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, signal) => { signal.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            await ModSideCommand.RunAsync(command, commandArguments, options.Project,
                context is null ? Environment.GetEnvironmentVariable("MODPACKTOOLS_PROJECT") : context.ActiveProject,
                configurationDirectory, cancellation.Token);
        }
        finally { Console.CancelKeyPress -= cancel; }
        return 0;
    }
    if (command.Equals("project", StringComparison.OrdinalIgnoreCase) && commandArguments.FirstOrDefault() is { } initialization && new[] { "register", "create" }.Contains(initialization, StringComparer.OrdinalIgnoreCase)
        && !commandArguments.Contains("--help", StringComparer.OrdinalIgnoreCase))
    {
        var configurationDirectory = Environment.GetEnvironmentVariable("MODPACKTOOLS_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configurationDirectory)) configurationDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools");
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, signal) => { signal.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            if (initialization.Equals("create", StringComparison.OrdinalIgnoreCase)) await ProjectCreationCommand.RunAsync(command, commandArguments, options.Project, configurationDirectory, cancellation.Token, !options.NoHuman);
            else await ProjectRegistrationCommand.RunAsync(command, commandArguments, options.Project, configurationDirectory, cancellation.Token, !options.NoHuman);
        }
        finally { Console.CancelKeyPress -= cancel; }
        return 0;
    }
    if (command.Equals("config", StringComparison.OrdinalIgnoreCase) && !commandArguments.Contains("--help", StringComparer.OrdinalIgnoreCase))
    {
        var configurationDirectory = Environment.GetEnvironmentVariable("MODPACKTOOLS_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configurationDirectory)) configurationDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools");
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, signal) => { signal.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try { await ConfigurationCommand.RunAsync(command, commandArguments, options.Project, configurationDirectory, cancellation.Token, !options.NoHuman); }
        finally { Console.CancelKeyPress -= cancel; }
        return 0;
    }
    if (command.Equals("resource-pack", StringComparison.OrdinalIgnoreCase) && !commandArguments.Contains("--help", StringComparer.OrdinalIgnoreCase))
    {
        var configurationDirectory = Environment.GetEnvironmentVariable("MODPACKTOOLS_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configurationDirectory)) configurationDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools");
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, signal) => { signal.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            await ResourcePackCommand.RunAsync(command, commandArguments, options.Project,
                context is null ? Environment.GetEnvironmentVariable("MODPACKTOOLS_PROJECT") : context.ActiveProject,
                Path.GetFullPath(configurationDirectory), cancellation.Token);
        }
        finally { Console.CancelKeyPress -= cancel; }
        return 0;
    }
    if (command.Equals("category", StringComparison.OrdinalIgnoreCase) && commandArguments.FirstOrDefault() is { } classification
        && new[] { "assign", "clear", "remove", "create" }.Contains(classification, StringComparer.OrdinalIgnoreCase)
        && !commandArguments.Contains("--help", StringComparer.OrdinalIgnoreCase))
    {
        var configurationDirectory = Environment.GetEnvironmentVariable("MODPACKTOOLS_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configurationDirectory)) configurationDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools");
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, signal) => { signal.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            if (classification.Equals("create", StringComparison.OrdinalIgnoreCase))
                await CategoryCreationCommand.RunAsync(command, commandArguments, options.Project,
                    context is null ? Environment.GetEnvironmentVariable("MODPACKTOOLS_PROJECT") : context.ActiveProject,
                    Path.GetFullPath(configurationDirectory), cancellation.Token);
            else
                await ClassificationCommand.RunAsync(command, commandArguments, options.Project,
                    context is null ? Environment.GetEnvironmentVariable("MODPACKTOOLS_PROJECT") : context.ActiveProject,
                    Path.GetFullPath(configurationDirectory), cancellation.Token);
        }
        finally { Console.CancelKeyPress -= cancel; }
        return 0;
    }
    if (command.Equals("content", StringComparison.OrdinalIgnoreCase) && commandArguments.FirstOrDefault() is { } mutation
        && new[] { "add", "update", "pin", "unpin", "remove" }.Contains(mutation, StringComparer.OrdinalIgnoreCase)
        && !commandArguments.Contains("--help", StringComparer.OrdinalIgnoreCase))
    {
        var configurationDirectory = Environment.GetEnvironmentVariable("MODPACKTOOLS_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configurationDirectory)) configurationDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools");
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, signal) => { signal.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var active = context is null ? Environment.GetEnvironmentVariable("MODPACKTOOLS_PROJECT") : context.ActiveProject;
            if (mutation.Equals("remove", StringComparison.OrdinalIgnoreCase))
                await RemovalCommand.RunAsync(command, commandArguments, options.Project, active, configurationDirectory, cancellation.Token, !options.NoHuman, reviewToken);
            else if (mutation.Equals("pin", StringComparison.OrdinalIgnoreCase) || mutation.Equals("unpin", StringComparison.OrdinalIgnoreCase))
                await PinCommand.RunAsync(command, commandArguments, options.Project, active, Path.GetFullPath(configurationDirectory), cancellation.Token);
            else
                await ContentMutationCommand.RunAsync(command, commandArguments, options.Project, active, Path.GetFullPath(configurationDirectory), cancellation.Token, !options.NoHuman);
        }
        finally { Console.CancelKeyPress -= cancel; }
        return 0;
    }
    if (command.Equals("content", StringComparison.OrdinalIgnoreCase) &&
        commandArguments.FirstOrDefault()?.Equals("search", StringComparison.OrdinalIgnoreCase) == true &&
        !commandArguments.Contains("--help", StringComparer.OrdinalIgnoreCase))
    {
        var configurationDirectory = Environment.GetEnvironmentVariable("MODPACKTOOLS_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configurationDirectory))
            configurationDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools");
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, signal) => { signal.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            await SearchCommand.RunAsync(command, commandArguments, options.Project,
                context is null ? Environment.GetEnvironmentVariable("MODPACKTOOLS_PROJECT") : context.ActiveProject,
                Path.GetFullPath(configurationDirectory), cancellation.Token);
        }
        finally { Console.CancelKeyPress -= cancel; }
        return 0;
    }
    if (command.Equals("content", StringComparison.OrdinalIgnoreCase) &&
        commandArguments.FirstOrDefault()?.Equals("versions", StringComparison.OrdinalIgnoreCase) == true &&
        !commandArguments.Contains("--help", StringComparer.OrdinalIgnoreCase))
    {
        var configurationDirectory = Environment.GetEnvironmentVariable("MODPACKTOOLS_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configurationDirectory))
            configurationDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools");
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, signal) => { signal.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            await VersionListCommand.RunAsync(command, commandArguments, options.Project,
                context is null ? Environment.GetEnvironmentVariable("MODPACKTOOLS_PROJECT") : context.ActiveProject,
                Path.GetFullPath(configurationDirectory), cancellation.Token);
        }
        finally { Console.CancelKeyPress -= cancel; }
        return 0;
    }
    if (command.Equals("project", StringComparison.OrdinalIgnoreCase) &&
        commandArguments.FirstOrDefault()?.Equals("status", StringComparison.OrdinalIgnoreCase) == true &&
        !commandArguments.Contains("--help", StringComparer.OrdinalIgnoreCase))
    {
        foreach (var argument in commandArguments.Skip(1))
            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                var name = argument[2..].Split('=', 2)[0];
                throw new DomainException("Option.Unknown", $"Option '--{name}' is not recognized for this command", "run the command with --help", target: name);
            }
        if (commandArguments.Length != 1)
            throw new DomainException("Command.InvalidArguments", "The command arguments do not match the expected syntax", "modpack project status",
                targetData: commandArguments.Length == 2 ? new TextTarget(commandArguments[1]) : new TextListTarget(commandArguments.Skip(1).ToArray()));
        var activeProject = context is null ? Environment.GetEnvironmentVariable("MODPACKTOOLS_PROJECT") : context.ActiveProject;
        var effectiveId = ProjectSelection.EffectiveId(options.Project, activeProject);
        var configurationDirectory = Environment.GetEnvironmentVariable("MODPACKTOOLS_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configurationDirectory))
            configurationDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools");
        var catalog = ProjectReader.Discover(ConfigurationReader.ReadRoot(Path.GetFullPath(configurationDirectory)));
        var project = ProjectSelection.Find(catalog, effectiveId);
        ProjectStructure.Assert(project);
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, signal) => { signal.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var inventory = InventoryReader.Read(project);
            cancellation.Token.ThrowIfCancellationRequested();
            var envelope = new ProjectStatusEnvelope(1, true, command, commandArguments, new(PublicProject.From(project),
                new(inventory.Mods.Length, inventory.ResourceView.Active.Length, inventory.ResourceView.Inactive.Length, inventory.Shaders.Length)));
            using var output = new NativeOutputBuffer("Project status", "reduce the reported project data before retrying");
            using (var writer = new Utf8JsonWriter(output)) JsonSerializer.Serialize(writer, envelope, WireJsonContext.Default.ProjectStatusEnvelope);
            cancellation.Token.ThrowIfCancellationRequested();
            Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length));
            Console.WriteLine();
        }
        finally { Console.CancelKeyPress -= cancel; }
        return 0;
    }
    if (command.Equals("category", StringComparison.OrdinalIgnoreCase) &&
        commandArguments.FirstOrDefault()?.Equals("list", StringComparison.OrdinalIgnoreCase) == true &&
        !commandArguments.Contains("--help", StringComparer.OrdinalIgnoreCase))
    {
        foreach (var argument in commandArguments.Skip(1))
            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                var name = argument[2..].Split('=', 2)[0];
                throw new DomainException("Option.Unknown", $"Option '--{name}' is not recognized for this command", "run the command with --help", target: name);
            }
        if (commandArguments.Length != 1)
            throw new DomainException("Command.InvalidArguments", "The command arguments do not match the expected syntax", "modpack category list",
                targetData: commandArguments.Length == 2 ? new TextTarget(commandArguments[1]) : new TextListTarget(commandArguments.Skip(1).ToArray()));
        var activeProject = context is null ? Environment.GetEnvironmentVariable("MODPACKTOOLS_PROJECT") : context.ActiveProject;
        var effectiveId = ProjectSelection.EffectiveId(options.Project, activeProject);
        var configurationDirectory = Environment.GetEnvironmentVariable("MODPACKTOOLS_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configurationDirectory))
            configurationDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools");
        configurationDirectory = Path.GetFullPath(configurationDirectory);
        var catalog = ProjectReader.Discover(ConfigurationReader.ReadRoot(configurationDirectory));
        var project = ProjectSelection.Find(catalog, effectiveId);
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, signal) => { signal.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var view = CategoryList.Read(project);
            cancellation.Token.ThrowIfCancellationRequested();
            CategoryReferenceCache.Write(configurationDirectory, view, DateTimeOffset.UtcNow);
            var rows = view.Categories.Select(row => new PublicCategory(row.ReferenceNumber, row.Id, row.Name, row.Order, row.ModCount, row.IsUnclassified)).ToArray();
            var envelope = new CategoryListEnvelope(1, true, command, commandArguments, new(new(PublicProject.From(project), rows)));
            using var output = new NativeOutputBuffer("Category list", "reduce the category list before retrying");
            using (var writer = new Utf8JsonWriter(output)) JsonSerializer.Serialize(writer, envelope, WireJsonContext.Default.CategoryListEnvelope);
            cancellation.Token.ThrowIfCancellationRequested();
            Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length));
            Console.WriteLine();
        }
        finally { Console.CancelKeyPress -= cancel; }
        return 0;
    }
    if (command.Equals("content", StringComparison.OrdinalIgnoreCase) &&
        commandArguments.FirstOrDefault()?.Equals("list", StringComparison.OrdinalIgnoreCase) == true &&
        !commandArguments.Contains("--help", StringComparer.OrdinalIgnoreCase))
    {
        var configurationDirectory = Environment.GetEnvironmentVariable("MODPACKTOOLS_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configurationDirectory))
            configurationDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools");
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, signal) => { signal.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            await ContentListCommand.RunAsync(command, commandArguments, options.Project,
                context is null ? Environment.GetEnvironmentVariable("MODPACKTOOLS_PROJECT") : context.ActiveProject,
                Path.GetFullPath(configurationDirectory), cancellation.Token, !options.NoHuman);
        }
        finally { Console.CancelKeyPress -= cancel; }
        return 0;
    }
    if (command.Equals("--version", StringComparison.OrdinalIgnoreCase))
    {
        var version = ProductVersion.Read(commandArguments, options.Project);
        var envelope = new VersionEnvelope(1, true, command, commandArguments, new(version));
        Console.WriteLine(JsonSerializer.Serialize(envelope, WireJsonContext.Default.VersionEnvelope));
        return 0;
    }
    if (command.Equals("project", StringComparison.OrdinalIgnoreCase) &&
        commandArguments.FirstOrDefault()?.Equals("use", StringComparison.OrdinalIgnoreCase) == true &&
        !commandArguments.Contains("--help", StringComparer.OrdinalIgnoreCase))
    {
        if (options.Project is not null)
            throw new DomainException("Option.ForbiddenCombination", "Option '--project' is not valid for this operation", "modpack project use <id>", target: options.Project);
        if (commandArguments.Length != 2)
            throw new DomainException("Command.InvalidArguments", "The command arguments do not match the expected syntax", "modpack project use <id>", targetData: commandArguments.Length == 1 ? null : new TextListTarget(commandArguments.Skip(1).ToArray()));
        if (commandArguments[1].Length == 0)
            throw new DomainException("Project.InvalidSelection", "The project ID cannot be empty", "modpack project use <id>", target: "");
        var activeProject = context is null ? Environment.GetEnvironmentVariable("MODPACKTOOLS_PROJECT") : context.ActiveProject;
        var effectiveId = ProjectSelection.EffectiveId(commandArguments[1], activeProject);
        var configurationDirectory = Environment.GetEnvironmentVariable("MODPACKTOOLS_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configurationDirectory))
            configurationDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools");
        var catalog = ProjectReader.Discover(ConfigurationReader.ReadRoot(Path.GetFullPath(configurationDirectory)));
        var selected = ProjectSelection.Find(catalog, effectiveId);
        var envelope = new SessionEnvelope(1, true, command, commandArguments, new(selected.Id), options.NoHuman ? null : new("session", selected.Name));
        Console.WriteLine(JsonSerializer.Serialize(envelope, WireJsonContext.Default.SessionEnvelope));
        return 0;
    }
    if (command.Equals("project", StringComparison.OrdinalIgnoreCase) &&
        commandArguments.FirstOrDefault()?.Equals("current", StringComparison.OrdinalIgnoreCase) == true &&
        !commandArguments.Contains("--help", StringComparer.OrdinalIgnoreCase))
    {
        if (options.Project is not null)
            throw new DomainException("Option.ForbiddenCombination", "Option '--project' is not valid for this operation", "modpack project current", target: options.Project);
        if (commandArguments.Length != 1)
            throw new DomainException("Command.InvalidArguments", "The command arguments do not match the expected syntax", "modpack project current", targetData: commandArguments.Length == 2 ? new TextTarget(commandArguments[1]) : new TextListTarget(commandArguments.Skip(1).ToArray()));
        var activeProject = context is null ? Environment.GetEnvironmentVariable("MODPACKTOOLS_PROJECT") : context.ActiveProject;
        var envelope = new SessionEnvelope(1, true, command, commandArguments, new(activeProject));
        Console.WriteLine(JsonSerializer.Serialize(envelope, WireJsonContext.Default.SessionEnvelope));
        return 0;
    }
    if (command.Equals("project", StringComparison.OrdinalIgnoreCase) &&
        commandArguments.FirstOrDefault()?.Equals("list", StringComparison.OrdinalIgnoreCase) == true &&
        !commandArguments.Contains("--help", StringComparer.OrdinalIgnoreCase))
    {
        if (options.Project is not null)
            throw new DomainException("Option.ForbiddenCombination", "Option '--project' is not valid for this operation", "modpack project list", target: options.Project);
        if (commandArguments.Length != 1)
            throw new DomainException("Command.InvalidArguments", "The command arguments do not match the expected syntax", "modpack project list", targetData: commandArguments.Length == 2 ? new TextTarget(commandArguments[1]) : new TextListTarget(commandArguments.Skip(1).ToArray()));
        var configurationDirectory = Environment.GetEnvironmentVariable("MODPACKTOOLS_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configurationDirectory))
            configurationDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackTools");
        var catalog = ProjectReader.Discover(ConfigurationReader.ReadRoot(Path.GetFullPath(configurationDirectory)));
        var envelope = new ProjectListEnvelope(1, true, command, commandArguments, new(new(catalog.Root, catalog.Projects.Select(PublicProject.From).ToArray())));
        Console.WriteLine(JsonSerializer.Serialize(envelope, WireJsonContext.Default.ProjectListEnvelope));
        return 0;
    }
}
catch (OperationCanceledException)
{
    var cancelled = FailureEnvelope.Create(command, commandArguments,
        Failure.From("ModpackTools.Core.OperationCancelled", "Listing was cancelled. Try: retry when ready.", "OperationStopped", null));
    Console.WriteLine(JsonSerializer.Serialize(cancelled, WireJsonContext.Default.FailureEnvelope));
    return 1;
}
catch (DomainException exception)
{
    var message = Sentence(exception.Message);
    if (!string.IsNullOrWhiteSpace(exception.Details)) message += Environment.NewLine + "Details: " + Sentence(exception.Details);
    message += Environment.NewLine + "Try: " + exception.Hint;
    var domainError = FailureEnvelope.Create(command, commandArguments,
        Failure.From(exception.Id, message, exception.Category, exception.Target));
    Console.WriteLine(JsonSerializer.Serialize(domainError, WireJsonContext.Default.FailureEnvelope));
    return 1;
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or FormatException or OverflowException)
{
    var inputError = FailureEnvelope.Create(command, commandArguments,
        Failure.From("Core.InputReadFailed", "Could not read project data. " + exception.Message + Environment.NewLine + "Try: check the file paths, data types and access permissions", "InvalidData", null));
    Console.WriteLine(JsonSerializer.Serialize(inputError, WireJsonContext.Default.FailureEnvelope));
    return 1;
}

var failure = FailureEnvelope.Create(command, commandArguments,
    Failure.From("Core.CapabilityUnavailable", "This native host currently supports version, project list, project current, project use, project status, content list, content versions, content search and category list.", "InvalidOperation", null));
Console.WriteLine(JsonSerializer.Serialize(failure, WireJsonContext.Default.FailureEnvelope));
return 1;

static string Sentence(string text)
{
    var result = text.Trim();
    return result.EndsWith('.') || result.EndsWith('!') || result.EndsWith('?') ? result : result + ".";
}
