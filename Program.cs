using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Lavalink;
using DSharpPlus.Net;
using Foxite.Common.Notifications;
using IkIheMusicBot.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qmmands;

namespace IkIheMusicBot {
	public sealed class Program {
		public static string ProgramVersion => "0.1.24";
	
		// ReSharper disable AccessToDisposedClosure
		private static async Task Main(string[] args) {
			IHost host = Host.CreateDefaultBuilder()
				.UseConsoleLifetime()
				.ConfigureAppConfiguration(configBuilder => {
					configBuilder.SetBasePath(Directory.GetCurrentDirectory());
					configBuilder.AddNewtonsoftJsonFile("appsettings.json");
					configBuilder.AddEnvironmentVariables("IKIHE_");
					configBuilder.AddCommandLine(args);
				})
				.ConfigureServices((ctx, isc) => {
					isc.Configure<DiscordConfiguration>(ctx.Configuration.GetSection("DiscordConfiguration"));
					isc.Configure<CommandServiceConfiguration>(ctx.Configuration.GetSection("CommandServiceConfiguration"));
					isc.Configure<DjRoleConfig>(ctx.Configuration.GetSection("DjRoleConfig"));
					isc.Configure<LocalMediaConfig>(ctx.Configuration.GetSection("LocalMediaConfig"));
					isc.Configure<LavalinkConfig>(ctx.Configuration.GetSection("LavalinkConfig"));
					isc.Configure<StartupConfig>(ctx.Configuration.GetSection("StartupConfig"));

					isc.AddSingleton(isp => new DiscordClient(new DSharpPlus.DiscordConfiguration() {
						Token = isp.GetRequiredService<IOptions<DiscordConfiguration>>().Value.Token,
						LoggerFactory = isp.GetRequiredService<ILoggerFactory>()
					}));
					isc.AddSingleton(isp => isp.GetRequiredService<DiscordClient>().UseLavalink());
					isc.AddSingleton<LavalinkManager>();

					isc.AddSingleton(isp => new CommandService(isp.GetRequiredService<IOptions<CommandServiceConfiguration>>().Value));
					isc.AddSingleton<CommandManager>();
					
					isc.AddSingleton<DjRoleService>();

					isc.AddNotifications()
						.AddDiscord(ctx.Configuration.GetSection("Notifications").GetSection("Discord"));
				})
				.Build();

			var discord = host.Services.GetRequiredService<DiscordClient>();
			var lavalink = host.Services.GetRequiredService<LavalinkExtension>();

			discord.Ready += (o, eventArgs) => {
				_ = Task.Run(async () => {
					StartupConfig startupConfig = host.Services.GetRequiredService<IOptions<StartupConfig>>().Value;
					if (startupConfig != null && startupConfig.JoinGuild != default) {
						try {
							var lavalinkManager = host.Services.GetRequiredService<LavalinkManager>();
							DiscordGuild guild = await discord.GetGuildAsync(startupConfig.JoinGuild);
							IReadOnlyList<DiscordChannel> channels = await guild.GetChannelsAsync();
							IReadOnlyList<LavalinkTrack> tracks = await lavalinkManager.QueueAsync(channels.First(channel => channel.Id == startupConfig.JoinChannel), startupConfig.LoadTrack, LavalinkSearchType.Plain);
							lavalinkManager.SetRepeating(guild, startupConfig.Repeat);
						} catch (Exception e) {
							host.Services.GetRequiredService<ILogger<Program>>().LogCritical(e, "Failed to perform startup actions");
							await host.Services.GetRequiredService<NotificationService>().SendNotificationAsync("Failed to perform startup actions\n" + e.ToStringDemystified());
						}
					}
				});
				return Task.CompletedTask;
			};

			discord.ClientErrored += (_, eventArgs) => {
				var logger = host.Services.GetRequiredService<ILogger<Program>>();
				try {
					return host.Services.GetRequiredService<NotificationService>().SendNotificationAsync($"ClientErrored event for {eventArgs.EventName}: {eventArgs.Exception}");
				} catch (Exception e) {
					// well shit
					logger.LogCritical(e, "Could not send notification for ClientErrored event, you done fucked up");
					throw;
				}
			};

			discord.InteractionCreated += (DiscordClient sender, InteractionCreateEventArgs eventArgs) => {
				if (eventArgs.Interaction.Type == InteractionType.ApplicationCommand && eventArgs.Interaction.ApplicationId == sender.CurrentApplication.Id) {
					eventArgs.Handled = true;
					_ = HandleCommandAsync(host.Services, eventArgs.Interaction);
				}
				return Task.CompletedTask;
			};
			
			Task startDiscord = discord.ConnectAsync();
			
			var commandManager = host.Services.GetRequiredService<CommandManager>();
			commandManager.Loading += (_, service, _) => service.AddModules(Assembly.GetExecutingAssembly());
			await commandManager.ReloadCommandsAsync(startDiscord);
			
			LavalinkConfig lavalinkConfig = host.Services.GetRequiredService<IOptions<LavalinkConfig>>().Value;

			var endpoint = new ConnectionEndpoint {
				Hostname = lavalinkConfig.Hostname,
				Port = lavalinkConfig.Port
			};

			await lavalink.ConnectAsync(new LavalinkConfiguration {
				Password = lavalinkConfig.Password,
				RestEndpoint = endpoint,
				SocketEndpoint = endpoint
			});

			await host.RunAsync();
			
			host.Dispose();
		}

		private static async Task HandleCommandAsync(IServiceProvider services, DiscordInteraction interaction) {
			var logger = services.GetRequiredService<ILogger<Program>>();
			await interaction.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
			try {
				int optionCount = (interaction.Data.Options ?? Array.Empty<DiscordInteractionDataOption>()).Count();
				Command? command = services.GetRequiredService<CommandService>().FindCommands(interaction.Data.Name)
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
					var ctx = new DiscordCommandContext(services, interaction, member);
					IResult result = await command.ExecuteAsync(parameters, ctx);
					if (result is CommandExecutionFailedResult cefr) {
						logger.LogError(cefr.Exception, "Error while executing command");
						await interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder() {
							Content = "There was an unhandled error while executing the command.",
						});
						await services.GetRequiredService<NotificationService>().SendNotificationAsync(cefr.Exception.ToStringDemystified());
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
				logger.LogCritical(e, "Error while handling command");
				await services.GetRequiredService<NotificationService>().SendNotificationAsync(e.ToStringDemystified());
				await interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder() {
					Content = "There was a serious unhandled error while executing the command.",
				});
			}
		}
	}
}
