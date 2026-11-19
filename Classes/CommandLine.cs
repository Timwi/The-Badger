using RT.CommandLine;
using RT.Services;

namespace Badger.Classes
{
    public class CommandLine
    {
        [IsMandatory, IsPositional]
        public Cmd Command;

        public int Execute()
        {
            return Command.Execute();
        }
    }

    [CommandGroup]
    public abstract class Cmd
    {
        public abstract int Execute();
    }

    [CommandName("uninstall"), DocumentationLiteral("Uninstalls the Windows service, if installed.")]
    public class CmdUnInstall : Cmd
    {
        public override int Execute()
        {
            new SingleSelfServiceProcess<BadgerService>().Uninstall();
            return 0;
        }
    }

    public abstract class CmdWithConfig : Cmd
    {
        [IsPositional, IsMandatory, DocumentationLiteral("Specifies the path and filename for the JSON file containing the bot configuration.")]
        public string ConfigFile;
    }

    [CommandName("install"), DocumentationLiteral("Installs this Discord bot as a Windows service.")]
    public class CmdInstall : CmdWithConfig
    {
        public override int Execute()
        {
            new SingleSelfServiceProcess<BadgerService>().Install(ServiceAccount.NetworkService, @$"runservice ""{ConfigFile}""");
            return 0;
        }
    }

    [CommandName("run"), DocumentationLiteral("Runs the Discord bot on the command-line.")]
    public class CmdRun : CmdWithConfig
    {
        public override int Execute()
        {
            new Bot(ConfigFile).RunAsync().GetAwaiter().GetResult();
            return 0;
        }
    }

    [CommandName("runservice"), Undocumented]
    public class CmdRunService : CmdWithConfig
    {
        public override int Execute()
        {
            BadgerService.ConfigFile = ConfigFile;
            new SingleSelfServiceProcess<BadgerService>().ExecuteServices();
            return 0;
        }
    }
}
