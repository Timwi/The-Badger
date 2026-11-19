using DSharpPlus.Entities;

namespace Badger.Classes
{
    public class Game
    {
        public string Value { get; set; }
        public string DisplayName { get; set; }
        public ulong ID { get; set; }
        public int MinPlayers { get; set; }
        public List<DiscordUser> Players { get; set; }
        public string Duration { get; set; }
        public string Description { get; set; }
        public string Emoji { get; set; }
        public string Comments { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ScheduledFor { get; set; }
        public DateTime DeleteAt { get; set; }
        public bool Pinged { get; set; }
        public bool Announced { get; set; }
        public Timer? Timer { get; set; }
        public Timer? PingTimer { get; set; }
        public DiscordMessage? Message { get; set; }
        public int DbId { get; set; }
        public Game(string value, string displayName, ulong id, int minPlayers = -1, string duration = "N/A", string description = "", string emoji = "")
        {
            Value = value;
            DisplayName = displayName;
            ID = id;
            MinPlayers = minPlayers;
            Timer = null;
            Message = null;
            Players = new List<DiscordUser>();
            Duration = duration;
            Description = description;
            Emoji = emoji;
            Comments = "";
        }

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

    public class NewGame
    {
        public Game Game { get; set; }
        public string Name { get; set; }
        public string Duration { get; set; }
        public string Comments { get; set; }
        public List<DiscordUser> Players { get; set; }
        public DateTime? ScheduledFor { get; set; }
        public DateTime DeleteAt { get; set; }
        public bool Pinged { get; set; }
        public bool Announced { get; set; }
        public Timer? Timer { get; set; }
        public Timer? PingTimer { get; set; }
        public DiscordMessage? Message { get; set; }
        public DateTime Created { get; }
        public int DbId { get; set; }
        public NewGame(Game game, string name, string comments = "", string duration = "Indefinite")
        {
            Game = game;
            Name = name;
            Comments = comments;
            Duration = duration;
            Timer = null;
            Message = null;
            Players = new List<DiscordUser>();
            Created = DateTime.Now;
        }
    }
}
