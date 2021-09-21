using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using Qmmands;

namespace IkIheMusicBot {
	public class CommandManager {
		private readonly CommandService m_CommandService;
		private readonly DiscordClient m_DiscordClient;
		private readonly Dictionary<string, Command> m_CommandMap;

		public event Action<CommandManager, CommandService, DiscordClient>? Loading;

		public CommandManager(CommandService commandService, DiscordClient discordClient) {
			m_CommandService = commandService;
			m_DiscordClient = discordClient;
			m_CommandMap = new Dictionary<string, Command>();
		}
		
		internal async Task ReloadCommandsAsync(Task? discordTask) {
			m_CommandService.RemoveAllModules();
			m_CommandService.RemoveAllTypeParsers();
			
			Loading?.Invoke(this, m_CommandService, m_DiscordClient);
			
			// TODO support subcommands and subcommand groups
			var typemap = new Dictionary<Type, ApplicationCommandOptionType>() {
				{ typeof(sbyte), ApplicationCommandOptionType.Integer },
				{ typeof(byte), ApplicationCommandOptionType.Integer },
				{ typeof(short), ApplicationCommandOptionType.Integer },
				{ typeof(ushort), ApplicationCommandOptionType.Integer },
				{ typeof(int), ApplicationCommandOptionType.Integer },
				{ typeof(uint), ApplicationCommandOptionType.Integer },
				{ typeof(long), ApplicationCommandOptionType.Integer },
				{ typeof(ulong), ApplicationCommandOptionType.Integer },
				{ typeof(float), ApplicationCommandOptionType.Number },
				{ typeof(double), ApplicationCommandOptionType.Number },
				{ typeof(decimal), ApplicationCommandOptionType.Number },
				{ typeof(bool), ApplicationCommandOptionType.Boolean },
				{ typeof(DiscordChannel), ApplicationCommandOptionType.Channel },
				{ typeof(DiscordUser), ApplicationCommandOptionType.User },
				{ typeof(DiscordRole), ApplicationCommandOptionType.Role },
				//{ typeof(IMention), ApplicationCommandOptionType.Mentionable }
			};

			var preCommands = new Dictionary<string, (DiscordApplicationCommand, Command)>(
				from command in m_CommandService.GetAllCommands()
				select new KeyValuePair<string, (DiscordApplicationCommand, Command)>(command.Name, (new DiscordApplicationCommand(
					command.Name,
					command.Description ?? "",
					from param in command.Parameters
					select new DiscordApplicationCommandOption(
						param.Name,
						param.Description ?? "",
						typemap.TryGetValue(param.Type, out ApplicationCommandOptionType type) ? type : ApplicationCommandOptionType.String,
						!param.IsOptional
					),
					true // command.Attributes.OfType<DefaultEnabledAttribute>().Any()
				), command))
			);

			m_CommandMap.Clear();
			if (discordTask != null) {
				await discordTask;
			}
			
			//*
			Dictionary<string, DiscordApplicationCommand>? existingCommands = (await m_DiscordClient.GetGlobalApplicationCommandsAsync()).ToDictionary(command => command.Name, command => command);
			IEnumerable<string> commandsMissingFromDiscord = preCommands.Select(kvp => kvp.Key).Except(existingCommands.Select(kvp => kvp.Key));
			IEnumerable<string> extraneousCommandsOnDiscord = existingCommands.Select(kvp => kvp.Key).Except(preCommands.Select(kvp => kvp.Key));

			foreach (string missingCommand in commandsMissingFromDiscord) {
				await m_DiscordClient.CreateGlobalApplicationCommandAsync(preCommands[missingCommand].Item1);
			}
			foreach (string extraneousCommand in extraneousCommandsOnDiscord) {
				await m_DiscordClient.DeleteGlobalApplicationCommandAsync(existingCommands[extraneousCommand].Id);
			}//*/
			
			/*	// Doesn't seem to work as advertised. This has the effect of destroying all commands, including the ones which already exist.
				// This causes ALL commands to become unusable until the cache expires.
			foreach (DiscordApplicationCommand command in await m_DiscordClient.BulkOverwriteGlobalApplicationCommandsAsync(preCommandMap.Select(kvp => kvp.Value.Item1))) {
				m_CommandMap[command.Name] = preCommandMap[command.Name].Item2;
			}//*/
		}
		
		public Command? GetCommand(string name) {
			bool result = m_CommandMap.TryGetValue(name, out Command? command);
			return command;
		}
	}
}
