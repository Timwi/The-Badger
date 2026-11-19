using System.Text;
using RT.Services;

namespace Badger
{
    class Program
    {
        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; }
            catch { }

            var serviceProcess = new SingleSelfServiceProcess<BadgerService>();
            if (args.Length == 1 && args[0].Equals("install", StringComparison.OrdinalIgnoreCase))
            {
                serviceProcess.Install(ServiceAccount.LocalService, BadgerService.ServiceArguments);
                return 0;
            }
            if (args.Length == 1 && args[0].Equals("uninstall", StringComparison.OrdinalIgnoreCase))
            {
                serviceProcess.Uninstall();
                return 0;
            }
            if (args.Length == 1 && args[0] == BadgerService.ServiceArguments)
            {
                Environment.CurrentDirectory = AppContext.BaseDirectory;
                serviceProcess.ExecuteServices();
                return 0;
            }

            new Bot().RunAsync().GetAwaiter().GetResult();
            return 0;
        }
    }
}
