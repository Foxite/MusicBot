using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using Foxite.Common.Notifications;
using Microsoft.Extensions.Logging;
using Qmmands;

namespace IkIheMusicBot {
	public class CommandHandler {
		private readonly ILogger<CommandHandler> m_Logger;
		private readonly CommandService m_Commands;
		private readonly NotificationService m_Notifications;
		private readonly IServiceProvider m_ServiceProvider;

		public CommandHandler(ILogger<CommandHandler> logger, CommandService commands, NotificationService notifications, IServiceProvider serviceProvider) {
			m_Logger = logger;
			m_Commands = commands;
			m_Notifications = notifications;
			m_ServiceProvider = serviceProvider;
		}
		
		public async Task HandleCommandAsync(DiscordInteraction interaction) {
			await interaction.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
			try {
				int optionCount = (interaction.Data.Options ?? Array.Empty<DiscordInteractionDataOption>()).Count();
				Command? command = m_Commands.FindCommands(interaction.Data.Name)
					.FirstOrDefault(commandMatch => optionCount == commandMatch.Command.Parameters.Count)?.Command;
				if (command != null) {
					IEnumerable<object> parameters = (interaction.Data.Options ?? Array.Empty<DiscordInteractionDataOption>()).Select(option => option.Type switch {
						// TODO implement proper type parsers
						//ApplicationCommandOptionType.Channel => ctx.Channel.Guild.GetChannel((ulong) option.Value),
						//ApplicationCommandOptionType.User => discord.GetUserAsync((ulong) option.Value).GetAwaiter().GetResult(), // deadlock?
						//ApplicationCommandOptionType.Role => ctx.Channel.Guild.GetRole((ulong) option.Value),
						_ => option.Value
					});
					DiscordMember member = await interaction.Guild.GetMemberAsync(interaction.User.Id);
					var ctx = new DiscordCommandContext(m_ServiceProvider, interaction, member);
					IResult result = await command.ExecuteAsync(parameters, ctx);
					if (result is CommandExecutionFailedResult cefr) {
						m_Logger.LogError(cefr.Exception, "Error while executing command");
						await interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder() {
							Content = "There was an unhandled error while executing the command.",
						});
						await m_Notifications.SendNotificationAsync(cefr.Exception.ToStringDemystified());
					} else if (result is ChecksFailedResult cfr) {
						var response = new StringBuilder("One or more checks have failed: ");
						if (cfr.FailedChecks.Count > 1) {
							response.AppendLine();
							foreach ((Qmmands.CheckAttribute _, CheckResult checkResult) in cfr.FailedChecks.Where(tuple => !tuple.Result.IsSuccessful)) {
								response.AppendLine($"- {checkResult.FailureReason}");
							}
						} else {
							response.Append(cfr.FailedChecks[0].Result.FailureReason);
						}
						await interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder() {
							Content = response.ToString(),
						}); 
					} else if (result is SuccessfulResult) {
						await interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder() {
							Content = "No result (successful)"
						});
					} else if (result is CommandResult cr) {
						var messageBuilder = new DiscordFollowupMessageBuilder();
						await cr.HandleAsync(ctx, messageBuilder);
						await interaction.CreateFollowupMessageAsync(messageBuilder);
					} else {
						throw new Exception("Invalid result type " + (result?.GetType().FullName ?? "null"));
					}
				} else {
					await interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder() {
						Content = "Parameter mismatch."
					});
				}
			} catch (Exception e) {
				m_Logger.LogCritical(e, "Error while handling command");
				await m_Notifications.SendNotificationAsync(e.ToStringDemystified());
				await interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder() {
					Content = "There was a serious unhandled error while executing the command.",
				});
			}
		}
	}
}
