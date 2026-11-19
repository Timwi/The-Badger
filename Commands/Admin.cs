using DSharpPlus.Entities;
using DSharpPlus;
using DSharpPlus.SlashCommands;
using Badger.Class;
using DSharpPlus.CommandsNext.Converters;
using Badger.Classes;

namespace Badger.Commands
{
    public class Admin : ApplicationCommandModule
    {
        [SlashCommand("generategamepicker", "Regenerates the game picker message in the looking-to-play channel.")]
        [SlashCommandPermissions(Permissions.Administrator)]
        public async Task GenerateGamePicker(InteractionContext ctx,
            [Option("messageid", "ID of an existing picker message in the looking-to-play channel to update in place.")] string? messageId = null)
        {
            try
            {
                await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource, new DiscordInteractionResponseBuilder() { IsEphemeral = true });

                string action;
                if (messageId != null)
                {
                    await Interactions.UpdatePickerMessageAsync(ulong.Parse(messageId));
                    action = "updated";
                }
                else
                {
                    await Interactions.RegeneratePickerAsync();
                    action = "created";
                }

                DiscordEmbedBuilder embed = new()
                {
                    Color = new DiscordColor("#FFFFFF"),
                    Title = $"__**Notice:**__",
                    Description = $"Game picker message {action}.",
                    Footer = new DiscordEmbedBuilder.EmbedFooter
                    {
                        Text = $"Server Time: {DateTime.Now}"
                    }
                };
                await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                await Util.ThrowError(ctx, ex);
            }
        }
    }
}
