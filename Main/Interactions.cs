using System;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Badger.Class;
using Badger.Classes;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using Microsoft.VisualBasic;

namespace Badger
{
    public class Interactions
    {
        private static readonly Game[] _games = new[]
        {
            new Game("mao", "Mao", 692901690483146762, 4, "Indefinite", "The game about figuring out the rules.", "🦡"),
            new Game("tac", "TAC", 692901782611034173, 4, "c.1-2 hours", "A 4-player strategy board game with cards.", "🎴"),
            new Game("social", "Social Deduction", 692901751094902895, 4, description: "e.g. Secret Hitler, etc.", emoji: "👤"),
            new Game("betrayal", "Betrayal", 692904039620411422, 4, "1+ hours", "Betrayal at the house on the hill. A game where you build your own haunted house for 3-6 players.", "🏚️"),
            new Game("talking", "Talking Games", 692905746740019320, 3, description: "e.g. Challenge & Contact, Wikipedia guessing games, etc.", emoji: "🎺"),
            new Game("trivia", "Trivia Games", 786195625300983838, 3, description: "e.g. Know-it-All, Jeopardy, Trivial Pursuit, etc.", emoji: "💡"),
            new Game("foursouls", "Four Souls", 1157247148610551873, 2, "1+ hours", "The Binding of Isaac: Four Souls card game.", "💀"),
            new Game("bombbusters", "Bomb Busters", 1370462503670382593, 4, description: "A co-operative bomb defusal game.", emoji: "💣"),
            new Game("jigsaw", "Jigsaw", 1400890756687855697, 2, description: "Work together to complete a jigsaw puzzle.", emoji: "🧩"),
            new Game("new", "New Games", 1119710634532143164, description: "Suggest a game not listed above.", emoji: "🎲"),
        };

        private static List<NewGame> newGames = new List<NewGame>();

        private static DiscordMessage? _pickerMessage;
        private static readonly SemaphoreSlim _postLock = new(1, 1);
        private static bool _startupDone = false;
        private static bool _restoreComplete = false;
        // A scheduled game only re-pings the role at its scheduled time if the schedule gave at
        // least this much of a gap since the creation post's role ping.
#if DEBUG
        private static readonly TimeSpan PostLifetime = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan RolePingMinimum = TimeSpan.FromMinutes(2);
#else
        private static readonly TimeSpan PostLifetime = TimeSpan.FromHours(2);
        private static readonly TimeSpan RolePingMinimum = TimeSpan.FromHours(2);
#endif

        public async Task AssignAllInteractions()
        {
            Bot.Client.InteractionCreated += LogInteractions;
            Bot.Client.VoiceStateUpdated += async (c, e) =>
            {
                await UpdateVoiceChannels(e);
            };

            Bot.Client.ComponentInteractionCreated += async (c, e) =>
            {
                if (e.Id == "plusButton")
                {
                    await AfterJoinGameSelect(e, true);
                }
                if (e.Id == "minusButton")
                {
                    await AfterJoinGameSelect(e, false);
                }
                if (e.Id == "gamemode_select" && _games.Any(x => x.Value == e.Values[0]))
                {
                    await AfterGameSelect(e);
                }
            };

            Bot.Client.ModalSubmitted += async (c, e) =>
            {
                if (e.Interaction.Data.CustomId == "newGameModal")
                {
                    await AfterNewGameSubmit(e);
                }
                else if (e.Interaction.Data.CustomId.EndsWith("GameModal"))
                {
                    await AfterGameSubmit(e);
                }
            };

            await Task.CompletedTask;
        }

        public static DiscordMessageBuilder BuildGamePickerMessage()
        {
            DiscordEmbedBuilder embed = new()
            {
                Color = new DiscordColor("#FFFFFF"),
                Title = "__**Choose a game to play:**__",
                Description = "*Each post will last for 2 hours before it is no longer interactable (scheduled games last until 2 hours after their scheduled time). " +
                "You can optionally schedule a game up to 1 day ahead. " +
                "You cannot ping for the same game while it is still looking for players.*"
            };

            IEnumerable<DiscordSelectComponentOption> options = _games.Select(game => new DiscordSelectComponentOption(
                label: game.DisplayName,
                value: game.Value,
                description: game.Description,
                emoji: game.Emoji == "" ? null : new DiscordComponentEmoji(game.Emoji)));

            DiscordSelectComponent select = new("gamemode_select", "Choose a game", options);

            return new DiscordMessageBuilder()
                .AddEmbed(embed)
                .AddComponents(select);
        }

        // Caller must hold _postLock.
        private static async Task RepostPickerAsync(DiscordChannel ltpChannel)
        {
            if (_pickerMessage != null)
            {
                try { await _pickerMessage.DeleteAsync(); }
                catch (NotFoundException) { }
                _pickerMessage = null;
            }
            _pickerMessage = await ltpChannel.SendMessageAsync(BuildGamePickerMessage());
        }

        public static async Task RegeneratePickerAsync()
        {
            await _postLock.WaitAsync();
            try
            {
                DiscordChannel ltpChannel = (await Bot.Client.GetGuildAsync(GuildID.TTS)).GetChannel(ChannelID.LTP);
                await RepostPickerAsync(ltpChannel);
            }
            finally
            {
                _postLock.Release();
            }
        }

        public static async Task UpdatePickerMessageAsync(ulong messageId)
        {
            await _postLock.WaitAsync();
            try
            {
                DiscordChannel ltpChannel = (await Bot.Client.GetGuildAsync(GuildID.TTS)).GetChannel(ChannelID.LTP);
                DiscordMessage message = await ltpChannel.GetMessageAsync(messageId);
                await message.ModifyAsync(BuildGamePickerMessage());
                _pickerMessage = message;
            }
            finally
            {
                _postLock.Release();
            }
        }

        private static DiscordEmbed BuildJoinPostEmbed(string displayName, string duration, string comments, List<DiscordUser> players, DateTime? scheduledFor, bool locked)
        {
            string playerList = "";
            if (players.Count > 0)
            {
                foreach (DiscordUser player in players)
                {
                    playerList += $"\n{player.Mention}";
                }
            }
            else
            {
                playerList = "\n*No players*";
            }

            return new DiscordEmbedBuilder()
            {
                Color = new DiscordColor("#FFFFFF"),
                Description = $"# Let's Play {displayName}:" +
                    (locked
                        ? $"\nYou have reached the minimum/recommended number of players required to play!"
                        : $"\nPress the + button to join up to play.") +
                    (scheduledFor != null
                        ? $"\n## Scheduled for: {Formatter.Timestamp(scheduledFor.Value, TimestampFormat.RelativeTime)} — {Formatter.Timestamp(scheduledFor.Value, TimestampFormat.ShortDateTime)}"
                        : "") +
                    $"\n## Duration: *{duration}*" +
                    $"\n## Additional comments:" +
                    $"\n*{comments}*" +
                    $"\n## Players:" +
                    $"{playerList}",
                Footer = new DiscordEmbedBuilder.EmbedFooter
                {
                    Text = $"Server Time: {DateTime.Now}"
                }
            };
        }

        private static async Task SendEphemeralErrorAsync(DiscordInteraction interaction, string description)
        {
            DiscordFollowupMessageBuilder error = new DiscordFollowupMessageBuilder() { IsEphemeral = true }
                .AddEmbed(new DiscordEmbedBuilder()
                {
                    Color = new DiscordColor("#FFFFFF"),
                    Title = "Error:",
                    Description = description,
                    Footer = new DiscordEmbedBuilder.EmbedFooter
                    {
                        Text = $"Server Time: {DateTime.Now}"
                    }
                });
            await interaction.CreateFollowupMessageAsync(error);
        }

        private static string SerializePlayers(List<DiscordUser> players)
        {
            return string.Join(",", players.Select(p => p.Id));
        }

        private static int InsertGamePost(GamePost row)
        {
            using BadgerContext db = new();
            db.GamePosts.Add(row);
            db.SaveChanges();
            return row.Id;
        }

        private static void UpdateGamePost(int dbId, Action<GamePost> update)
        {
            if (dbId == 0) return;
            using BadgerContext db = new();
            GamePost? row = db.GamePosts.Find(dbId);
            if (row == null) return;
            update(row);
            db.SaveChanges();
        }

        private static void DeleteGamePost(int dbId)
        {
            if (dbId == 0) return;
            using BadgerContext db = new();
            GamePost? row = db.GamePosts.Find(dbId);
            if (row == null) return;
            db.GamePosts.Remove(row);
            db.SaveChanges();
        }

        private static void GameTimerCallback(object? obj)
        {
            _ = Task.Run(async () =>
            {
                await _postLock.WaitAsync();
                try
                {
                    await RunDeleteLogicAsync(obj);
                }
                catch (Exception ex)
                {
                    await Util.ThrowInteractionlessError(ex);
                }
                finally
                {
                    _postLock.Release();
                }
            });
        }

        // Caller must hold _postLock.
        private static async Task RunDeleteLogicAsync(object? obj)
        {
            if (obj is Game game)
            {
                if (game.Message != null)
                {
                    // Clean up state and the DB row even if the message delete fails — a lingering
                    // message a mod can remove beats a game wedged behind a stale pending gate.
                    try
                    {
                        await game.Message.ModifyAsync(
                            embed: game.Message.Embeds[0]
                        );
                    }
                    catch (NotFoundException) { }
                    catch (Exception ex) { await Util.ThrowInteractionlessError(ex); }
                }
                DeleteGamePost(game.DbId);
                game.ResetPost();
            }
            else if (obj is NewGame newGame)
            {
                if (newGame.Message != null)
                {
                    try
                    {
                        await newGame.Message.ModifyAsync(
                            embed: newGame.Message.Embeds[0]
                        );
                    }
                    catch (NotFoundException) { }
                    catch (Exception ex) { await Util.ThrowInteractionlessError(ex); }
                }
                DeleteGamePost(newGame.DbId);
                newGame.Timer?.Dispose();
                newGame.Timer = null;
                newGame.PingTimer?.Dispose();
                newGame.PingTimer = null;
                newGames.Remove(newGame);
            }
        }

        private static void PingTimerCallback(object? obj)
        {
            _ = Task.Run(async () =>
            {
                await _postLock.WaitAsync();
                try
                {
                    await RunScheduledPingLogicAsync(obj);
                }
                catch (Exception ex)
                {
                    await Util.ThrowInteractionlessError(ex);
                }
                finally
                {
                    _postLock.Release();
                }
            });
        }

        // Caller must hold _postLock.
        private static async Task RunScheduledPingLogicAsync(object? obj)
        {
            if (obj is Game game)
            {
                game.PingTimer?.Dispose();
                game.PingTimer = null;
                if (game.Pinged || game.Message == null) return;

                if (game.Players.Count >= game.MinPlayers)
                {
                    game.Pinged = true;
                    game.Announced = true;
                    UpdateGamePost(game.DbId, r => { r.Pinged = true; r.Announced = true; });
                    await FirePlayersPingAndLockAsync(game.DisplayName, game.Duration, game.Comments, game.Players, game.ScheduledFor, game.Message);
                }
                else
                {
                    game.Announced = true;
                    UpdateGamePost(game.DbId, r => r.Announced = true);

                    string playerList = string.Concat(game.Players.Select(p => $"\n{p.Mention}"));
                    DiscordEmbedBuilder embed = new()
                    {
                        Color = new DiscordColor("#FFFFFF"),
                        Description = $"# Let's Play {game.DisplayName}:" +
                            $"\nIt's time to play, but we are still looking for {game.MinPlayers - game.Players.Count} more player(s)!" +
                            $"\nPress the + button on the post above to join.",
                        Footer = new DiscordEmbedBuilder.EmbedFooter
                        {
                            Text = $"Server Time: {DateTime.Now}"
                        }
                    };

                    bool rolePing = game.ID != 0 && game.ScheduledFor != null
                        && game.ScheduledFor.Value - game.CreatedAt >= RolePingMinimum;

                    string content = (rolePing ? $"<@&{game.ID}>" : "") + playerList;
#if DEBUG == false
                    List<IMention> mentions = game.Players.Select(p => (IMention)new UserMention(p.Id)).ToList();
                    if (rolePing)
                    {
                        mentions.Add(new RoleMention(game.ID));
                    }
#endif
                    DiscordMessageBuilder messageBuilder = new DiscordMessageBuilder()
                        .WithContent(content)
#if DEBUG == false
                        .WithAllowedMentions(mentions)
#endif
                        .AddEmbed(embed);

                    TimerMessage timerMessage = new TimerMessage(await game.Message.Channel.SendMessageAsync(messageBuilder));
                    await RepostPickerAsync(game.Message.Channel);
                }
            }
            else if (obj is NewGame newGame)
            {
                newGame.PingTimer?.Dispose();
                newGame.PingTimer = null;
                if (newGame.Pinged || newGame.Message == null) return;

                if (newGame.Players.Count >= 2)
                {
                    newGame.Pinged = true;
                    newGame.Announced = true;
                    UpdateGamePost(newGame.DbId, r => { r.Pinged = true; r.Announced = true; });
                    await FirePlayersPingAndLockAsync(newGame.Name, newGame.Duration, newGame.Comments, newGame.Players, newGame.ScheduledFor, newGame.Message);
                }
                else
                {
                    newGame.Announced = true;
                    UpdateGamePost(newGame.DbId, r => r.Announced = true);
                }
            }
        }

        // Caller must hold _postLock and have set the Pinged/Announced flags (memory + DB) already.
        private static async Task FirePlayersPingAndLockAsync(string displayName, string duration, string comments, List<DiscordUser> players, DateTime? scheduledFor, DiscordMessage message)
        {
            // WithContent preserves the role-mention line (a builder edit with no content would
            // clear it); if the post was manually deleted, the players still get their ping.
            try
            {
                await message.ModifyAsync(new DiscordMessageBuilder()
                    .WithContent(message.Content)
                    .AddEmbed(BuildJoinPostEmbed(displayName, duration, comments, players, scheduledFor, locked: true)));
            }
            catch (NotFoundException) { }

            string playerList = "";
            foreach (DiscordUser player in players)
            {
                playerList += $"\n{player.Mention}";
            }

            DiscordEmbedBuilder finalEmbed = new()
            {
                Color = new DiscordColor("#FFFFFF"),
                Description = $"# Let's Play {displayName}:" +
                    $"\nYou have reached the minimum/recommended number of players required to play!",
                Footer = new DiscordEmbedBuilder.EmbedFooter
                {
                    Text = $"Server Time: {DateTime.Now}"
                }
            };

#if DEBUG == false
            IMention[] userMentions = players.Select(p => (IMention)new UserMention(p.Id)).ToArray();
#endif
            DiscordMessageBuilder messageBuilder = new DiscordMessageBuilder()
                .WithContent(playerList)
#if DEBUG == false
                .WithAllowedMentions(userMentions)
#endif
                .AddEmbed(finalEmbed);

            TimerMessage timerMessage = new TimerMessage(await message.Channel.SendMessageAsync(messageBuilder));
            await RepostPickerAsync(message.Channel);
        }

        private async Task AfterGameSubmit(ModalSubmitEventArgs eventArgs)
        {
            await eventArgs.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);

            await _postLock.WaitAsync();
            try
            {
                if (!_restoreComplete)
                {
                    await SendEphemeralErrorAsync(eventArgs.Interaction, "The bot is still starting up — try again in a moment.");
                    return;
                }

                Game game = _games.First(x => x.Value == eventArgs.Interaction.Data.CustomId.Replace("GameModal", ""));

                if (game.Timer != null && !game.Pinged)
                {
                    await SendEphemeralErrorAsync(eventArgs.Interaction, $"{game.DisplayName} already has a pending notification.");
                    return;
                }

                eventArgs.Values.TryGetValue("schedule", out string? scheduleInput);
                if (!Util.TryParseSchedule(scheduleInput, out TimeSpan delay, out string? scheduleError))
                {
                    await SendEphemeralErrorAsync(eventArgs.Interaction, scheduleError!);
                    return;
                }

                if (game.Timer != null && game.Pinged)
                {
                    // The previous post already locked in its players — replace it with the new round.
                    await RunDeleteLogicAsync(game);
                }

                game.ResetPost();
                game.Players.Add(eventArgs.Interaction.User);
                eventArgs.Values.TryGetValue("comments", out string? commentsInput);
                game.Comments = string.IsNullOrEmpty(commentsInput) ? "No additional comments" : commentsInput;
                game.CreatedAt = DateTime.Now;
                game.ScheduledFor = delay == TimeSpan.Zero ? null : game.CreatedAt + delay;
                game.DeleteAt = (game.ScheduledFor ?? game.CreatedAt) + PostLifetime;

                DiscordChannel logChannel = Bot.Client.GetGuildAsync(GuildID.TTS).Result.GetChannel(ChannelID.BOTLOGS);
                await logChannel.SendMessageAsync(new DiscordEmbedBuilder()
                {
                    Color = new DiscordColor("#FFFFFF"),
                    Title = $"__**Notice:**__",
                    Description = $"{eventArgs.Interaction.User.Mention} is looking to play {game.DisplayName}" +
                        (game.ScheduledFor != null ? $" at {game.ScheduledFor}" : "") + ".",
                    Footer = new DiscordEmbedBuilder.EmbedFooter
                    {
                        Text = $"Server Time: {DateTime.Now}"
                    }
                });

                DiscordButtonComponent plusButton = new(ButtonStyle.Success, "plusButton", "", false, new DiscordComponentEmoji(DiscordEmoji.FromName(Bot.Client, ":heavy_plus_sign:")));
                DiscordButtonComponent minusButton = new(ButtonStyle.Danger, "minusButton", "", false, new DiscordComponentEmoji(DiscordEmoji.FromName(Bot.Client, ":heavy_minus_sign:")));

                DiscordMessageBuilder messageBuilder = new DiscordMessageBuilder()
                .AddEmbed(BuildJoinPostEmbed(game.DisplayName, game.Duration, game.Comments, game.Players, game.ScheduledFor, locked: false))
                .AddComponents(plusButton, minusButton);
#if DEBUG == false
                if (game.ID != 0)
                {
                    messageBuilder
                        .WithContent($"<@&{game.ID}>")
                        .WithAllowedMentions(new IMention[] { new RoleMention(game.ID) });
                }
#endif

                DiscordChannel ltpChannel = Bot.Client.GetGuildAsync(GuildID.TTS).Result.GetChannel(ChannelID.LTP);
                game.Message = await ltpChannel.SendMessageAsync(messageBuilder);

                try
                {
                    game.DbId = InsertGamePost(new GamePost
                    {
                        GameValue = game.Value,
                        Comments = game.Comments,
                        MessageId = unchecked((long)game.Message.Id),
                        ChannelId = unchecked((long)ltpChannel.Id),
                        CreatedAt = game.CreatedAt,
                        ScheduledFor = game.ScheduledFor,
                        DeleteAt = game.DeleteAt,
                        Pinged = false,
                        Announced = false,
                        PlayerIds = SerializePlayers(game.Players),
                    });
                }
                catch (Exception ex)
                {
                    // Without a DB row the post can neither expire nor survive a restart — remove it
                    // rather than leave an untracked orphan in the channel.
                    try { await game.Message.DeleteAsync(); }
                    catch (NotFoundException) { }
                    game.ResetPost();
                    await SendEphemeralErrorAsync(eventArgs.Interaction, "Something went wrong creating the post — please try again.");
                    await Util.ThrowInteractionlessError(ex);
                    return;
                }

                game.Timer = new Timer(GameTimerCallback, game, (long)Math.Max(0, (game.DeleteAt - DateTime.Now).TotalMilliseconds), Timeout.Infinite);
                if (game.ScheduledFor != null)
                {
                    game.PingTimer = new Timer(PingTimerCallback, game, (long)Math.Max(0, (game.ScheduledFor.Value - DateTime.Now).TotalMilliseconds), Timeout.Infinite);
                }

                await RepostPickerAsync(ltpChannel);
            }
            catch (Exception ex)
            {
                await Util.ThrowInteractionlessError(ex);
            }
            finally
            {
                _postLock.Release();
            }
        }

        private async Task AfterNewGameSubmit(ModalSubmitEventArgs eventArgs)
        {
            await eventArgs.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);

            await _postLock.WaitAsync();
            try
            {
                if (!_restoreComplete)
                {
                    await SendEphemeralErrorAsync(eventArgs.Interaction, "The bot is still starting up — try again in a moment.");
                    return;
                }

                Game game = _games.First(x => x.Value == "new");

                eventArgs.Values.TryGetValue("schedule", out string? scheduleInput);
                if (!Util.TryParseSchedule(scheduleInput, out TimeSpan delay, out string? scheduleError))
                {
                    await SendEphemeralErrorAsync(eventArgs.Interaction, scheduleError!);
                    return;
                }

                eventArgs.Values.TryGetValue("comments", out string? commentsInput);
                eventArgs.Values.TryGetValue("duration", out string? durationInput);
                NewGame newGame = new NewGame(game, eventArgs.Values["name"],
                    string.IsNullOrEmpty(commentsInput) ? "No additional comments" : commentsInput,
                    string.IsNullOrEmpty(durationInput) ? "Not specified" : durationInput);
                newGame.Players.Add(eventArgs.Interaction.User);
                newGame.ScheduledFor = delay == TimeSpan.Zero ? null : newGame.Created + delay;
                newGame.DeleteAt = (newGame.ScheduledFor ?? newGame.Created) + PostLifetime;

                DiscordChannel logChannel = Bot.Client.GetGuildAsync(GuildID.TTS).Result.GetChannel(ChannelID.BOTLOGS);
                await logChannel.SendMessageAsync(new DiscordEmbedBuilder()
                {
                    Color = new DiscordColor("#FFFFFF"),
                    Title = $"__**Notice:**__",
                    Description = $"{eventArgs.Interaction.User.Mention} is looking to play {game.DisplayName} - {newGame.Name}" +
                        (newGame.ScheduledFor != null ? $" at {newGame.ScheduledFor}" : "") + ".",
                    Footer = new DiscordEmbedBuilder.EmbedFooter
                    {
                        Text = $"Server Time: {DateTime.Now}"
                    }
                });

                DiscordButtonComponent plusButton = new(ButtonStyle.Success, "plusButton", "", false, new DiscordComponentEmoji(DiscordEmoji.FromName(Bot.Client, ":heavy_plus_sign:")));
                DiscordButtonComponent minusButton = new(ButtonStyle.Danger, "minusButton", "", false, new DiscordComponentEmoji(DiscordEmoji.FromName(Bot.Client, ":heavy_minus_sign:")));

                DiscordMessageBuilder messageBuilder = new DiscordMessageBuilder()
                .AddEmbed(BuildJoinPostEmbed(newGame.Name, newGame.Duration, newGame.Comments, newGame.Players, newGame.ScheduledFor, locked: false))
                .AddComponents(plusButton, minusButton);
#if DEBUG == false
                messageBuilder
                    .WithContent($"<@&{game.ID}> {newGame.Name}")
                    .WithAllowedMentions(new IMention[] { new RoleMention(game.ID) });
#endif

                DiscordChannel ltpChannel = Bot.Client.GetGuildAsync(GuildID.TTS).Result.GetChannel(ChannelID.LTP);
                newGame.Message = await ltpChannel.SendMessageAsync(messageBuilder);

                try
                {
                    newGame.DbId = InsertGamePost(new GamePost
                    {
                        GameValue = game.Value,
                        CustomName = newGame.Name,
                        CustomDuration = newGame.Duration,
                        Comments = newGame.Comments,
                        MessageId = unchecked((long)newGame.Message.Id),
                        ChannelId = unchecked((long)ltpChannel.Id),
                        CreatedAt = newGame.Created,
                        ScheduledFor = newGame.ScheduledFor,
                        DeleteAt = newGame.DeleteAt,
                        Pinged = false,
                        Announced = false,
                        PlayerIds = SerializePlayers(newGame.Players),
                    });
                }
                catch (Exception ex)
                {
                    try { await newGame.Message.DeleteAsync(); }
                    catch (NotFoundException) { }
                    await SendEphemeralErrorAsync(eventArgs.Interaction, "Something went wrong creating the post — please try again.");
                    await Util.ThrowInteractionlessError(ex);
                    return;
                }

                newGame.Timer = new Timer(GameTimerCallback, newGame, (long)Math.Max(0, (newGame.DeleteAt - DateTime.Now).TotalMilliseconds), Timeout.Infinite);
                if (newGame.ScheduledFor != null)
                {
                    newGame.PingTimer = new Timer(PingTimerCallback, newGame, (long)Math.Max(0, (newGame.ScheduledFor.Value - DateTime.Now).TotalMilliseconds), Timeout.Infinite);
                }

                newGames.Add(newGame);

                await RepostPickerAsync(ltpChannel);
            }
            catch (Exception ex)
            {
                await Util.ThrowInteractionlessError(ex);
            }
            finally
            {
                _postLock.Release();
            }
        }

        private async Task AfterGameSelect(ComponentInteractionCreateEventArgs eventArgs)
        {
            if (!_restoreComplete)
            {
                await eventArgs.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
                await SendEphemeralErrorAsync(eventArgs.Interaction, "The bot is still starting up — try again in a moment.");
                await eventArgs.Message.ModifyAsync(eventArgs.Message.Embeds[0]);
                return;
            }

            Game game = _games.First(x => x.Value == eventArgs.Values[0]);

            if (game.Value == "new")
            {
                await eventArgs.Interaction.CreateResponseAsync(InteractionResponseType.Modal, new DiscordInteractionResponseBuilder()
                .WithTitle("New Game Information")
                .WithCustomId("newGameModal")
                .AddComponents(new TextInputComponent(label: "Name", customId: "name", placeholder: "TAC", required: true, style: TextInputStyle.Short, max_length: 100))
                .AddComponents(new TextInputComponent(label: "Expected Duration", customId: "duration", placeholder: "1 hour", required: false, style: TextInputStyle.Short, max_length: 100))
                .AddComponents(new TextInputComponent(label: "Additional Comments", customId: "comments", placeholder: "A strategy card game (similar to Sorry!)", required: false, style: TextInputStyle.Paragraph, max_length: 1024))
                .AddComponents(new TextInputComponent(label: "Schedule (optional)", customId: "schedule", placeholder: "e.g. in 30 minutes, 2 hours, 1 day", required: false, style: TextInputStyle.Short, max_length: 32))
                );
                await eventArgs.Message.ModifyAsync(eventArgs.Message.Embeds[0]);
            }
            else
            {
                if (game.Timer != null && !game.Pinged)
                {
                    await eventArgs.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
                    await SendEphemeralErrorAsync(eventArgs.Interaction, $"{game.DisplayName} already has a pending notification.");
                    await eventArgs.Message.ModifyAsync(eventArgs.Message.Embeds[0]);
                    return;
                }
                await eventArgs.Interaction.CreateResponseAsync(InteractionResponseType.Modal, new DiscordInteractionResponseBuilder()
                    .WithTitle("Accompanying Message")
                    .WithCustomId($"{game.Value}GameModal")
                    .AddComponents(new TextInputComponent(label: "Additional Comments", customId: "comments", placeholder: "Anyone looking to play some Mao? I have a couple of hours.", required: false, style: TextInputStyle.Paragraph, max_length: 1024))
                    .AddComponents(new TextInputComponent(label: "Schedule (optional)", customId: "schedule", placeholder: "e.g. in 30 minutes, 2 hours, 1 day", required: false, style: TextInputStyle.Short, max_length: 32))
                    );
                await eventArgs.Message.ModifyAsync(eventArgs.Message.Embeds[0]);
            }
        }

        private async Task AfterJoinGameSelect(ComponentInteractionCreateEventArgs eventArgs, bool add)
        {
            await eventArgs.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);

            await _postLock.WaitAsync();
            try
            {
                if (!_restoreComplete)
                {
                    await SendEphemeralErrorAsync(eventArgs.Interaction, "The bot is still starting up — try again in a moment.");
                    return;
                }

                Game? game = _games.FirstOrDefault(x => x.Message?.Id == eventArgs.Message.Id);
                NewGame? newGame = game == null ? newGames.FirstOrDefault(x => x.Message?.Id == eventArgs.Message.Id) : null;

                if (game == null && newGame == null)
                {
                    await SendEphemeralErrorAsync(eventArgs.Interaction, "This post is no longer active.");
                    return;
                }

                if (game != null ? game.Pinged : newGame!.Pinged) return;

                List<DiscordUser> players = game != null ? game.Players : newGame!.Players;

                if (add)
                {
                    if (players.Any(p => p.Id == eventArgs.User.Id)) return;
                    players.Add(eventArgs.User);
                }
                else
                {
                    DiscordUser? existing = players.FirstOrDefault(p => p.Id == eventArgs.User.Id);
                    if (existing == null) return;
                    players.Remove(existing);
                }

                UpdateGamePost(game != null ? game.DbId : newGame!.DbId, r => r.PlayerIds = SerializePlayers(players));

                string displayName = game != null ? game.DisplayName : newGame!.Name;
                string duration = game != null ? game.Duration : newGame!.Duration;
                string comments = game != null ? game.Comments : newGame!.Comments;
                DateTime? scheduledFor = game != null ? game.ScheduledFor : newGame!.ScheduledFor;

                try
                {
                    await eventArgs.Message.ModifyAsync(BuildJoinPostEmbed(displayName, duration, comments, players, scheduledFor, locked: false));
                }
                catch (NotFoundException)
                {
                    // The post was manually deleted — clean up its state and DB row.
                    await RunDeleteLogicAsync(game != null ? (object)game : newGame!);
                    return;
                }

                if (!add) return;

                if (game != null)
                {
                    bool pingDue = game.ScheduledFor == null || game.Announced;
                    if (pingDue && game.MinPlayers > 0 && players.Count >= game.MinPlayers)
                    {
                        game.Pinged = true;
                        game.Announced = true;
                        UpdateGamePost(game.DbId, r => { r.Pinged = true; r.Announced = true; });
                        await FirePlayersPingAndLockAsync(game.DisplayName, game.Duration, game.Comments, game.Players, game.ScheduledFor, game.Message!);
                    }
                }
                else if (newGame != null)
                {
                    if (newGame.ScheduledFor != null && newGame.Announced && players.Count >= 2)
                    {
                        newGame.Pinged = true;
                        UpdateGamePost(newGame.DbId, r => r.Pinged = true);
                        await FirePlayersPingAndLockAsync(newGame.Name, newGame.Duration, newGame.Comments, newGame.Players, newGame.ScheduledFor, newGame.Message!);
                    }
                }
            }
            catch (Exception ex)
            {
                await Util.ThrowInteractionlessError(ex);
            }
            finally
            {
                _postLock.Release();
            }
        }

        public static async Task OnStartupAsync()
        {
            if (_startupDone) return;
            _startupDone = true;

            await _postLock.WaitAsync();
            try
            {
                DiscordGuild guild = await Bot.Client.GetGuildAsync(GuildID.TTS);
                DiscordChannel ltpChannel = guild.GetChannel(ChannelID.LTP);

                List<GamePost> rows;
                using (BadgerContext db = new())
                {
                    rows = db.GamePosts.ToList();
                }

                foreach (GamePost row in rows)
                {
                    try
                    {
                        DiscordChannel channel = guild.GetChannel(unchecked((ulong)row.ChannelId)) ?? ltpChannel;

                        DiscordMessage message;
                        try
                        {
                            message = await channel.GetMessageAsync(unchecked((ulong)row.MessageId));
                        }
                        catch (NotFoundException)
                        {
                            DeleteGamePost(row.Id);
                            continue;
                        }

                        Game? game = _games.FirstOrDefault(x => x.Value == row.GameValue);
                        if (game == null)
                        {
                            try { await message.DeleteAsync(); }
                            catch (NotFoundException) { }
                            DeleteGamePost(row.Id);
                            continue;
                        }

                        List<DiscordUser> players = new();
                        foreach (string idString in row.PlayerIds.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (!ulong.TryParse(idString, out ulong userId)) continue;
                            try { players.Add(await Bot.Client.GetUserAsync(userId)); }
                            catch (NotFoundException) { }
                        }

                        object post;
                        if (game.Value == "new")
                        {
                            NewGame newGame = new NewGame(game, row.CustomName ?? "Unknown", row.Comments, row.CustomDuration ?? "Not specified")
                            {
                                Message = message,
                                ScheduledFor = row.ScheduledFor,
                                DeleteAt = row.DeleteAt,
                                Pinged = row.Pinged,
                                Announced = row.Announced,
                                DbId = row.Id,
                            };
                            newGame.Players.AddRange(players);
                            newGames.Add(newGame);
                            post = newGame;
                        }
                        else
                        {
                            game.ResetPost();
                            game.Message = message;
                            game.Comments = row.Comments;
                            game.CreatedAt = row.CreatedAt;
                            game.ScheduledFor = row.ScheduledFor;
                            game.DeleteAt = row.DeleteAt;
                            game.Pinged = row.Pinged;
                            game.Announced = row.Announced;
                            game.DbId = row.Id;
                            game.Players.AddRange(players);
                            post = game;
                        }

                        if (row.DeleteAt <= DateTime.Now)
                        {
                            await RunDeleteLogicAsync(post);
                            continue;
                        }

                        long deleteDue = (long)Math.Max(0, (row.DeleteAt - DateTime.Now).TotalMilliseconds);
                        if (post is Game postGame)
                        {
                            postGame.Timer = new Timer(GameTimerCallback, postGame, deleteDue, Timeout.Infinite);
                        }
                        else if (post is NewGame postNewGame)
                        {
                            postNewGame.Timer = new Timer(GameTimerCallback, postNewGame, deleteDue, Timeout.Infinite);
                        }

                        if (row.ScheduledFor != null && !row.Pinged)
                        {
                            if (row.ScheduledFor <= DateTime.Now)
                            {
                                if (!row.Announced)
                                {
                                    await RunScheduledPingLogicAsync(post);
                                }
                            }
                            else
                            {
                                long pingDue = (long)Math.Max(0, (row.ScheduledFor.Value - DateTime.Now).TotalMilliseconds);
                                if (post is Game pingGame)
                                {
                                    pingGame.PingTimer = new Timer(PingTimerCallback, pingGame, pingDue, Timeout.Infinite);
                                }
                                else if (post is NewGame pingNewGame)
                                {
                                    pingNewGame.PingTimer = new Timer(PingTimerCallback, pingNewGame, pingDue, Timeout.Infinite);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        await Util.ThrowInteractionlessError(ex);
                    }
                }

                IReadOnlyList<DiscordMessage> messages = await ltpChannel.GetMessagesAsync(100);
                foreach (DiscordMessage message in messages)
                {
                    if (message.Author != null && message.Author.Id == Bot.Client.CurrentUser.Id
                        && message.Components != null
                        && message.Components.Any(actionRow => actionRow.CustomId == "gamemode_select"))
                    {
                        try { await message.DeleteAsync(); }
                        catch (NotFoundException) { }
                    }
                }
                _pickerMessage = null;
                await RepostPickerAsync(ltpChannel);
            }
            catch (Exception ex)
            {
                await Util.ThrowInteractionlessError(ex);
            }
            finally
            {
                _restoreComplete = true;
                _postLock.Release();
            }
        }

        private async Task LogInteractions(DiscordClient client, InteractionCreateEventArgs eventArgs)
        {
            DiscordChannel channel = Bot.Client.GetGuildAsync(GuildID.TTS).Result.GetChannel(ChannelID.BOTLOGS);

            string options = "";

            if (eventArgs.Interaction.Data.Options != null)
            {
                foreach (DiscordInteractionDataOption option in eventArgs.Interaction.Data.Options)
                {
                    options += $" {option.Name}: *{option.Value}*";
                }
            }

            DiscordEmbedBuilder embed = new()
            {
                Color = new DiscordColor("#FFFFFF"),
                Title = $"__**Notice:**__",
                Description = $"'/{eventArgs.Interaction.Data.Name}{options}' was used by {eventArgs.Interaction.User.Mention}.",
                Footer = new DiscordEmbedBuilder.EmbedFooter
                {
                    Text = $"Server Time: {DateTime.Now}"
                }
            };
            await channel.SendMessageAsync(embed);
        }

        private bool _updatingChannels = false;
        private bool _eventDuringChannelUpdate = false;

        private async Task UpdateVoiceChannels(VoiceStateUpdateEventArgs eventArgs)
        {
        again:
            if (_updatingChannels)
            {
                _eventDuringChannelUpdate = true;
                return;
            }
            _updatingChannels = true;
            try
            {
                // Check if the channel(s) has/have the format "<name> #<number>"
                string? channelName = null;
                Match m;
                if (eventArgs.Before != null && eventArgs.Before.Channel != null && (m = Regex.Match(eventArgs.Before.Channel.Name, @"^(.*) #\d+$")).Success)
                {
                    channelName = m.Groups[1].Value;
                    await UpdateVoiceChannelCollection(eventArgs.Guild, channelName);
                }
                if (eventArgs.After != null && eventArgs.After.Channel != null &&
                    (m = Regex.Match(eventArgs.After.Channel.Name, @"^(.*) #\d+$")).Success && m.Groups[1].Value != channelName)
                {
                    await UpdateVoiceChannelCollection(eventArgs.Guild, m.Groups[1].Value);
                }

                _updatingChannels = false;
                if (_eventDuringChannelUpdate)
                {
                    _eventDuringChannelUpdate = false;
                    goto again;
                }
            }
            catch (Exception ex)
            {
                await Util.ThrowInteractionlessError(ex);
            }
            finally
            {
                _updatingChannels = false;
                _eventDuringChannelUpdate = false;
            }
        }

        private static async Task UpdateVoiceChannelCollection(DiscordGuild guild, string name)
        {
            // Get a list of all voice channels with this name, their number, and the number of users in them
            var channelInfos = (await guild.GetChannelsAsync())
                .Select(ch => new { Channel = ch, Match = Regex.Match(ch.Name, $@"^{Regex.Escape(name)} #(\d+)$") })
                .Where(inf => inf.Match.Success)
                .Select(inf => new { inf.Channel, UserCount = inf.Channel.Users.Count, Number = int.Parse(inf.Match.Groups[1].Value) })
                .OrderBy(inf => inf.Number)
                .ToList();

            // Delete empty voice channels except for the lowest-numbered one
            bool isFirst = true;
            for (int chIx = 0; chIx < channelInfos.Count; chIx++)
            {
                if (channelInfos[chIx].UserCount == 0)
                {
                    if (isFirst)
                    {
                        isFirst = false;
                    }
                    else
                    {
                        DiscordChannel logChannel = Bot.Client.GetGuildAsync(GuildID.TTS).Result.GetChannel(ChannelID.BOTLOGS);
                        DiscordEmbedBuilder embed = new()
                        {
                            Color = new DiscordColor("#FFFFFF"),
                            Title = $"__**Notice:**__",
                            Description = $"Deleted VC {channelInfos[chIx].Channel.Name}.",
                            Footer = new DiscordEmbedBuilder.EmbedFooter
                            {
                                Text = $"Server Time: {DateTime.Now}"
                            }
                        };

                        await logChannel.SendMessageAsync(embed);

                        if (channelInfos[chIx].Channel.GetMessagesAsync(1000).Result.Count > 0)
                        {
                            string txtFile = $"Last 1000 Messages from {channelInfos[chIx].Channel.Name}:\r\n";
                            var messages = channelInfos[chIx].Channel.GetMessagesAsync(1000).Result.ToList();
                            messages.Reverse();
                            foreach (var message in messages)
                            {
                                txtFile += $"[{message.CreationTimestamp}] {message.Author.Username}: {message.Content}";
                                foreach (var attachment in message.Attachments)
                                {
                                    txtFile += $" {attachment.Url}";
                                }
                                txtFile += "\r\n";
                            }
                            string fileName = $"{DateTime.Now.ToString().Replace(":", "").Replace("/", "-")} - {channelInfos[chIx].Channel.Name}.txt";
                            await File.WriteAllTextAsync(fileName, txtFile);
                            Stream stream = File.Open(fileName, FileMode.Open);
                            await logChannel.SendMessageAsync(new DiscordMessageBuilder().AddFile(fileName, stream));
                            stream.Close();
                            await stream.DisposeAsync();
                            File.Delete(fileName);
                        }
                        await channelInfos[chIx].Channel.DeleteAsync();
                        channelInfos.RemoveAt(chIx);
                        chIx--;
                    }
                }
            }

            // Rename channels whose numbers are now out of order
            bool hasEmpty = false;
            int curNum = 1;
            foreach (var ch in channelInfos)
            {
                if (ch.Number != curNum)
                {
                    DiscordChannel logChannel = Bot.Client.GetGuildAsync(GuildID.TTS).Result.GetChannel(ChannelID.BOTLOGS);
                    DiscordEmbedBuilder embed = new()
                    {
                        Color = new DiscordColor("#FFFFFF"),
                        Title = $"__**Notice:**__",
                        Description = $"Renamed VC {ch.Channel.Name} to {name} #{curNum}.",
                        Footer = new DiscordEmbedBuilder.EmbedFooter
                        {
                            Text = $"Server Time: {DateTime.Now}"
                        }
                    };

                    await logChannel.SendMessageAsync(embed);

                    await ch.Channel.ModifyAsync(cem => cem.Name = $"{name} #{curNum}");
                }
                curNum++;
                hasEmpty = hasEmpty || ch.UserCount == 0;
            }

            // Create a new channel if no channels are empty
            if (!hasEmpty)
            {
                DiscordChannel lastChannel = channelInfos.Last().Channel;
                await guild.CreateChannelAsync($"{name} #{curNum}", ChannelType.Voice, parent: lastChannel.Parent, position: lastChannel.Position);

                DiscordChannel logChannel = Bot.Client.GetGuildAsync(GuildID.TTS).Result.GetChannel(ChannelID.BOTLOGS);
                DiscordEmbedBuilder embed = new()
                {
                    Color = new DiscordColor("#FFFFFF"),
                    Title = $"__**Notice:**__",
                    Description = $"Created VC {name} #{curNum}.",
                    Footer = new DiscordEmbedBuilder.EmbedFooter
                    {
                        Text = $"Server Time: {DateTime.Now}"
                    }
                };

                await logChannel.SendMessageAsync(embed);
            }
        }
    }
}
