using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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
					configBuilder.AddEnvironmentVariables("IKIHE_");
					configBuilder.AddCommandLine(args);
				})
				.ConfigureServices((ctx, isc) => {
					isc.Configure<DiscordConfiguration>(ctx.Configuration.GetSection("DiscordConfiguration"));
					isc.Configure<CommandServiceConfiguration>(ctx.Configuration.GetSection("CommandServiceConfiguration"));

					isc.AddSingleton(isp => new DiscordClient(new DSharpPlus.DiscordConfiguration() {
						Token = isp.GetRequiredService<IOptions<DiscordConfiguration>>().Value.Token
					}));
					isc.AddSingleton(isp => isp.GetRequiredService<DiscordClient>().UseLavalink());
					isc.AddSingleton<LavalinkManager>();

					isc.AddSingleton(isp => new CommandService(isp.GetRequiredService<IOptions<CommandServiceConfiguration>>().Value));
					isc.AddSingleton<CommandManager>();

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

			var endpoint = new ConnectionEndpoint {
				Hostname = "127.0.0.1", // From your server configuration.
				Port = 2333 // From your server configuration
			};

			var lavalinkConfig = new LavalinkConfiguration {
				Password = "youshallnotpass", // From your server configuration.
				RestEndpoint = endpoint,
				SocketEndpoint = endpoint
			};
			
			Task startDiscord = discord.ConnectAsync();
			
			var commandManager = host.Services.GetRequiredService<CommandManager>();
			commandManager.Loading += (_, service, _) => service.AddModules(Assembly.GetExecutingAssembly());
			await commandManager.ReloadCommandsAsync(startDiscord);
			
			await lavalink.ConnectAsync(lavalinkConfig);

			await host.RunAsync();
		}

		private static async Task HandleCommandAsync(IServiceProvider services, DiscordInteraction interaction) {
			await interaction.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
			try {
				Command command = services.GetRequiredService<CommandManager>().GetCommand(interaction.Data.Name)!;
				if ((interaction.Data.Options ?? Array.Empty<DiscordInteractionDataOption>()).CountEquals(command.Parameters.Count)) {
					var ctx = new DiscordCommandContext(services, interaction);
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
					Content = "There was an error while executing the command.",
					IsEphemeral = true
				});
				Console.WriteLine(e.ToStringDemystified());
			}
		}
	}
}
