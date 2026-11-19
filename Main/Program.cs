using System.Text;
using Badger.Classes;
using RT.CommandLine;
using RT.Services;

namespace Badger
{
    class Program
    {
        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; }
            catch { }

            CommandLine cmd;
            try { cmd = CommandLineParser.Parse<CommandLine>(args); }
            catch (CommandLineParseException exc)
            {
                exc.WriteUsageInfoToConsole();
                return 1;
            }

            return cmd.Execute();
        }
    }
}
