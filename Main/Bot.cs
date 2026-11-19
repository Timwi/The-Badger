using System.Text;
using Badger.Class;
using Badger.Classes;
using Badger.Commands;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Enums;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.SlashCommands;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Badger
{
    public class Bot(string configFilePath)
    {
        public Interactions interactions = new();
        public Events events = new();
        public static DiscordClient Client { get; private set; }
        public static SlashCommandsExtension SlashCommands { get; private set; }

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            var json = string.Empty;

            using (var fs = File.OpenRead(configFilePath))
            using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                json = await sr.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            var configJson = JsonConvert.DeserializeObject<BotConfig>(json);

            DiscordConfiguration config = new()
            {
                Token = configJson.Token,
                TokenType = TokenType.Bot,
                AutoReconnect = true,
                MinimumLogLevel = LogLevel.Debug
            };
            BadgerContext.DbConnectionString = configJson.ConnectionString;

            Client = new DiscordClient(config);

            Client.Ready += OnClientReady;

            Client.GuildDownloadCompleted += (c, e) =>
            {
                _ = Task.Run(Interactions.OnStartupAsync);
                return Task.CompletedTask;
            };

            Client.UseInteractivity(new InteractivityConfiguration
            {
                PollBehaviour = PollBehaviour.KeepEmojis,
                //AckPaginationButtons = true,
                Timeout = TimeSpan.FromSeconds(60)
            });

            CommandsNextConfiguration commandsConfig = new()
            {
                EnableDms = false,
                EnableDefaultHelp = false,
                DmHelp = true
            };

            SlashCommands = Client.UseSlashCommands();
#if DEBUG
            SlashCommands.RegisterCommands<Testing>();
#endif
            SlashCommands.RegisterCommands<Info>();
            SlashCommands.RegisterCommands<Admin>();

            await Events.AssignAllEvents();

            await interactions.AssignAllInteractions();

            DiscordActivity activity = new()
            {
                Name = $"figuring out the rules."
            };

            Data.InitializeDatabase();

            await Client.ConnectAsync(activity);

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                await Client.DisconnectAsync();
            }
        }

        private Task OnClientReady(DiscordClient sender, ReadyEventArgs e) => Task.CompletedTask;
    }
}
