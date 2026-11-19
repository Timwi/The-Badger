using Badger.Class;
using Badger.Classes;
using FluentScheduler;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Badger.Main
{
    public class ScheduledTasks
    {
        public static async Task InitializeTasks()
        {
            //var register = new Registry();
            //register.Schedule(async () => await ExecuteTimers()).ToRunEvery(1).Days().At(12, 30);
        }

        public static async Task ExecuteTimers()
        {
            try
            {
            }
            catch (Exception ex)
            {
                await Util.ThrowInteractionlessError(ex);
            }
        }
    }
}
