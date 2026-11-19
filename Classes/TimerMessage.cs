using DSharpPlus.Entities;

namespace Badger.Classes
{
    public class TimerMessage
    {
        // Roots live instances so the Timer cannot be garbage-collected (and silently
        // cancelled) before it fires.
        private static readonly HashSet<TimerMessage> _live = new();

        public Timer Timer { get; set; }
        public DiscordMessage Message { get; set; }
        public TimerMessage(DiscordMessage message)
        {
            Message = message;
            lock (_live)
            {
                _live.Add(this);
            }
            Timer = new Timer(TimerMessageCallback, this, 600000, 0);
        }
        private void TimerMessageCallback(object? obj)
        {
            TimerMessage timerMessage = (TimerMessage)obj;
            timerMessage.Message.DeleteAsync().ConfigureAwait(false);
            timerMessage.Timer.Dispose();
            lock (_live)
            {
                _live.Remove(timerMessage);
            }
        }
    }
}
