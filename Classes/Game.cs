using DSharpPlus.Entities;

namespace Badger.Classes
{
    public class Game(string value, string displayName, ulong id, int minPlayers = -1, string duration = "N/A", string description = "", string emoji = "")
    {
        public string Value { get; set; } = value;
        public string DisplayName { get; set; } = displayName;
        public ulong ID { get; set; } = id;
        public int MinPlayers { get; set; } = minPlayers;
        public List<DiscordUser> Players { get; set; } = [];
        public string Duration { get; set; } = duration;
        public string Description { get; set; } = description;
        public string Emoji { get; set; } = emoji;
        public string Comments { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime? ScheduledFor { get; set; }
        public DateTime DeleteAt { get; set; }
        public bool Pinged { get; set; }
        public bool Announced { get; set; }
        public Timer Timer { get; set; } = null;
        public Timer PingTimer { get; set; }
        public DiscordMessage Message { get; set; } = null;
        public int DbId { get; set; }

        public void ResetPost()
        {
            Timer?.Dispose();
            Timer = null;
            PingTimer?.Dispose();
            PingTimer = null;
            Message = null;
            Players.Clear();
            Comments = "";
            ScheduledFor = null;
            Pinged = false;
            Announced = false;
            DbId = 0;
        }
    }

    public class NewGame(Game game, string name, string comments = "", string duration = "Indefinite")
    {
        public Game Game { get; set; } = game;
        public string Name { get; set; } = name;
        public string Duration { get; set; } = duration;
        public string Comments { get; set; } = comments;
        public List<DiscordUser> Players { get; set; } = [];
        public DateTime? ScheduledFor { get; set; }
        public DateTime DeleteAt { get; set; }
        public bool Pinged { get; set; }
        public bool Announced { get; set; }
        public Timer Timer { get; set; } = null;
        public Timer PingTimer { get; set; }
        public DiscordMessage Message { get; set; } = null;
        public DateTime Created { get; } = DateTime.Now;
        public int DbId { get; set; }
    }
}
