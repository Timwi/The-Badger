using System.Diagnostics;
using RT.Services;

namespace Badger
{
    public class BadgerService : SelfService
    {
        public static string ConfigFile;

        private readonly Bot _bot = new(ConfigFile);
        private readonly CancellationTokenSource _stopping = new();
        private Task _runTask;

        public BadgerService()
        {
            ServiceName = "TheBadger";
            ServiceDisplayName = "The Badger Discord Bot";
            ServiceDescription = "Runs the Badger Discord bot.";
            ServiceStartMode = System.ServiceProcess.ServiceStartMode.Automatic;
        }

        protected override void OnStart(string[] args)
        {
            _runTask = Task.Run(() => _bot.RunAsync(_stopping.Token));
            _ = _runTask.ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    Trace.TraceError(task.Exception.ToString());
                    ExitCode = 1;
                    StopSelf();
                }
            }, TaskScheduler.Default);
        }

        protected override void OnStop()
        {
            _stopping.Cancel();
            _runTask?.GetAwaiter().GetResult();
            _stopping.Dispose();
        }
    }
}
