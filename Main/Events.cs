using Badger.Class;
using Badger.Classes;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;

namespace Badger
{
    public class Events
    {
        private static readonly RaidTracker _raidTracker = new(RaidGuard.ChannelThreshold, RaidGuard.Window);

        public static async Task AssignAllEvents()
        {
            Bot.Client.MessageCreated += async (c, e) => await CheckForChannelSpam(e);

            await Task.CompletedTask;
        }

        private static async Task CheckForChannelSpam(MessageCreateEventArgs e)
        {
            try
            {
                if (e.Guild == null || e.Guild.Id != GuildID.TTS)
                    return;
                if (e.Author.IsBot || e.Message.WebhookMessage)
                    return;

                if (!_raidTracker.RecordMessage(e.Author.Id, e.Channel.Id, e.Message.Id, e.Message.CreationTimestamp))
                    return;

                var member = e.Author as DiscordMember;
                if (member == null)
                {
                    try
                    {
                        member = await e.Guild.GetMemberAsync(e.Author.Id);
                    }
                    catch (NotFoundException)
                    {
                        _raidTracker.Forget(e.Author.Id);
                        return;
                    }
                }

                var exempt = member.Permissions.HasPermission(Permissions.Administrator);

#if DEBUG
                // Dry run: never ban, just log what would have happened. Forget so the tester can trip it again.
                _raidTracker.Forget(e.Author.Id);
                var description = $"[DEBUG] Would have banned {e.Author.Mention} ({e.Author.Username}, {e.Author.Id}) for posting in " +
                                     $"{RaidGuard.ChannelThreshold}+ channels within {RaidGuard.Window.TotalSeconds} seconds. " +
                                     $"Last channel: {e.Channel.Mention}." +
                                     (exempt ? " In a RELEASE build this user would have been exempt (Administrator)." : "");
#else
                if (exempt)
                {
                    _raidTracker.Forget(e.Author.Id);
                    return;
                }

                IReadOnlyList<(ulong ChannelId, ulong MessageId)> raidMessages = _raidTracker.GetRaidMessages(e.Author.Id);

                await e.Guild.BanMemberAsync(member, RaidGuard.BanDeleteMessageDays,
                    $"Anti-raid: messaged {RaidGuard.ChannelThreshold}+ channels within {RaidGuard.Window.TotalSeconds} seconds");
                _raidTracker.Forget(e.Author.Id);

                // The ban's own prune skips messages in voice channel chats (and threads), so delete the
                // tracked raid messages directly.
                int removed = 0;
                foreach ((ulong channelId, ulong messageId) in raidMessages)
                {
                    try
                    {
                        DiscordChannel? channel = e.Guild.GetChannel(channelId)
                        ?? (e.Guild.Threads.TryGetValue(channelId, out DiscordThreadChannel? thread) ? thread : null);
                        if (channel == null)
                            continue;
                        await channel.DeleteMessageAsync(await channel.GetMessageAsync(messageId), "Anti-raid cleanup");
                        removed++;
                    }
                    catch (NotFoundException)
                    {
                        // Already gone — the ban's prune got there first.
                        removed++;
                    }
                    catch (Exception)
                    {
                        // Leave the message for moderators; the count below will show the shortfall.
                    }
                }

                string description = $"Banned {e.Author.Mention} ({e.Author.Username}, {e.Author.Id}) for posting in " +
                                     $"{RaidGuard.ChannelThreshold}+ channels within {RaidGuard.Window.TotalSeconds} seconds. " +
                                     $"Removed {removed}/{raidMessages.Count} raid messages and requested a " +
                                     $"{RaidGuard.BanDeleteMessageDays}-day message prune. " +
                                     $"Last channel: {e.Channel.Mention}.";
#endif

                var logChannel = (await Bot.Client.GetGuildAsync(GuildID.TTS)).GetChannel(ChannelID.BOTLOGS);
                DiscordEmbedBuilder embed = new()
                {
                    Color = new DiscordColor("#FFFFFF"),
                    Title = $"__**Notice:**__",
                    Description = description,
                    Footer = new DiscordEmbedBuilder.EmbedFooter
                    {
                        Text = $"Server Time: {DateTime.Now}"
                    }
                };
                await logChannel.SendMessageAsync(embed);
            }
            catch (UnauthorizedException ex)
            {
                // Tripped state is intentionally kept so a failing ban is only reported once.
                await Util.ThrowInteractionlessError(new Exception(
                    $"Anti-raid ban of {e.Author.Username} ({e.Author.Id}) failed — check the bot has Ban Members and its role is above the user's.", ex));
            }
            catch (Exception ex)
            {
                await Util.ThrowInteractionlessError(ex);
            }
        }
    }
}
