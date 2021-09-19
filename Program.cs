using System;
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
using IkIheMusicBot.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Qmmands;

namespace IkIheMusicBot {
	public static class Program {
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

					isc.AddSingleton(isp => new DiscordClient(new DSharpPlus.DiscordConfiguration() {
						Token = isp.GetRequiredService<IOptions<DiscordConfiguration>>().Value.Token
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

			discord.InteractionCreated += (DiscordClient sender, InteractionCreateEventArgs eventArgs) => {
				if (eventArgs.Interaction.Type == InteractionType.ApplicationCommand && eventArgs.Interaction.ApplicationId == sender.CurrentApplication.Id) {
					eventArgs.Handled = true;
					_ = HandleCommandAsync(host.Services, eventArgs.Interaction);
				}
				return Task.CompletedTask;
			};
			
			LavalinkConfig lavalinkConfig = host.Services.GetRequiredService<IOptions<LavalinkConfig>>().Value;

			var endpoint = new ConnectionEndpoint {
				Hostname = lavalinkConfig.Hostname,
				Port = lavalinkConfig.Port
			};

			var lavalinkConfig2 = new LavalinkConfiguration {
				Password = lavalinkConfig.Password,
				RestEndpoint = endpoint,
				SocketEndpoint = endpoint
			};
			
			Task startDiscord = discord.ConnectAsync();
			
			var commandManager = host.Services.GetRequiredService<CommandManager>();
			commandManager.Loading += (_, service, _) => service.AddModules(Assembly.GetExecutingAssembly());
			await commandManager.ReloadCommandsAsync(startDiscord);
			
			await lavalink.ConnectAsync(lavalinkConfig2);

			await host.RunAsync();
		}

		private static async Task HandleCommandAsync(IServiceProvider services, DiscordInteraction interaction) {
			await interaction.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
			try {
				Command command = services.GetRequiredService<CommandManager>().GetCommand(interaction.Data.Name)!;
				if ((interaction.Data.Options ?? Array.Empty<DiscordInteractionDataOption>()).CountEquals(command.Parameters.Count)) {
					DiscordMember member = await interaction.Guild.GetMemberAsync(interaction.User.Id);
					var ctx = new DiscordCommandContext(services, interaction, member);
					IResult result = await command.ExecuteAsync(
						(interaction.Data.Options ?? Array.Empty<DiscordInteractionDataOption>()).Select(option => option.Type switch {
							//ApplicationCommandOptionType.Channel => ctx.Channel.Guild.GetChannel((ulong) option.Value),
							//ApplicationCommandOptionType.User => discord.GetUserAsync((ulong) option.Value).GetAwaiter().GetResult(), // deadlock?
							//ApplicationCommandOptionType.Role => ctx.Channel.Guild.GetRole((ulong) option.Value),
							_ => option.Value
						}),
						ctx
					);
					if (result is CommandExecutionFailedResult cefr) {
						Console.WriteLine(cefr.Exception.ToStringDemystified());
						await interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder() {
							Content = "There was a unhandled error while executing the command.",
						});
					} else if (result is ChecksFailedResult cfr) {
						var response = new StringBuilder("One or more checks have failed: ");
						if (cfr.FailedChecks.Count > 1) {
							response.AppendLine();
							foreach ((Qmmands.CheckAttribute check, CheckResult checkResult) in cfr.FailedChecks.Where(tuple => !tuple.Result.IsSuccessful)) {
								response.AppendLine($"- {checkResult.FailureReason}");
							}
						} else {
							response.Append(cfr.FailedChecks[0].Result.FailureReason);
						}
						await interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder() {
							Content = response.ToString(),
						}); 
					} else if (result is SuccessfulResult sr) {
						await interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder() {
							Content = "No result (successful)"
						});
					} else if (result is CommandResult cr) {
						var messageBuilder = new DiscordFollowupMessageBuilder();
						await cr.HandleAsync(ctx, messageBuilder);
						await interaction.CreateFollowupMessageAsync(messageBuilder);
					} else {
						throw new Exception("Invalid result type " + result?.GetType()?.FullName ?? "null");
					}
				} else {
					await interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder() {
						Content = "Parameter mismatch."
					});
				}
			} catch (Exception e) {
				await interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder() {
					Content = "There was a serious unhandled error while executing the command.",
				});
				Console.WriteLine(e.ToStringDemystified());
			}
		}
	}
}
