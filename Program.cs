using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Lavalink;
using DSharpPlus.Net;
using Foxite.Common.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Qmmands;

namespace IkIheMusicBot {
	public sealed class Program {
		public static string ProgramVersion => "0.1.23";
	
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

					isc.AddDbContext<QueueDbContext>(GetEntityFrameworkConfigurator(ctx.Configuration.GetSection("Database")));
					isc.AddSingleton<QueuePersistenceService>();

					isc.AddSingleton(isp => new DiscordClient(new DSharpPlus.DiscordConfiguration() {
						Token = isp.GetOptions<DiscordConfiguration>().Value.Token,
						LoggerFactory = isp.GetRequiredService<ILoggerFactory>()
					}));
					isc.AddSingleton(isp => isp.GetRequiredService<DiscordClient>().UseLavalink());
					isc.AddSingleton<LavalinkManager>();

					isc.AddSingleton(isp => new CommandService(isp.GetOptions<CommandServiceConfiguration>().Value));
					isc.AddSingleton<CommandManager>();
					isc.AddTransient<CommandHandler>();
					
					isc.AddSingleton<DjRoleService>();
					
					isc.AddNotifications()
						.AddDiscord(ctx.Configuration.GetSection("Notifications").GetSection("Discord"));
				})
				.Build();

			var discord = host.Services.GetRequiredService<DiscordClient>();
			var lavalink = host.Services.GetRequiredService<LavalinkExtension>();

			discord.Ready += (o, eventArgs) => {
				_ = Task.Run(() => host.Services.GetRequiredService<QueuePersistenceService>().RestartQueuesAsync());
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

			discord.InteractionCreated += (sender, eventArgs) => {
				if (eventArgs.Interaction.Type == InteractionType.ApplicationCommand && eventArgs.Interaction.ApplicationId == sender.CurrentApplication.Id) {
					eventArgs.Handled = true;
					_ = host.Services.GetRequiredService<CommandHandler>().HandleCommandAsync(eventArgs.Interaction);
				}
				return Task.CompletedTask;
			};
			
			Task startDiscord = discord.ConnectAsync();
			
			var commandManager = host.Services.GetRequiredService<CommandManager>();
			commandManager.Loading += (_, service, _) => service.AddModules(Assembly.GetExecutingAssembly());
			await commandManager.ReloadCommandsAsync(startDiscord);
			
			LavalinkConfig lavalinkConfig = host.Services.GetOptions<LavalinkConfig>().Value;

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

		public static Action<DbContextOptionsBuilder> GetEntityFrameworkConfigurator(IConfiguration config) {
			return dbOptions => ConfigureEntityFramework(dbOptions, config.Get<PostgresConfig>());
		}
		
		public static void ConfigureEntityFramework(DbContextOptionsBuilder dbOptions, PostgresConfig config) {
			var configDict = new Dictionary<string, object> {
				["Host"] = config.Host,
				["Port"] = config.Port,
				["Database"] = config.Database,
				["Username"] = config.Username,
				["Password"] = config.Password
			};

			if (config.Encrypt is EncryptMode.Standard or EncryptMode.Trust) {
				configDict["SSL Mode"] = "Require";
				if (config.Encrypt == EncryptMode.Trust) {
					configDict["Trust Server Certificate"] = true;
				}
			} else {
				configDict["SSL Mode"] = "Prefer";
			}
			
			string connectionString = string.Join("; ", configDict.Select(kvp => $"{kvp.Key}={kvp.Value}"));
			
			dbOptions.UseNpgsql(connectionString, npgsqlOptions => {
				npgsqlOptions.MigrationsAssembly(Assembly.GetExecutingAssembly().GetName().Name);
			});
		}
	}
}
