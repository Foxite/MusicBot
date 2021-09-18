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

			var preCommandMap = new Dictionary<string, (DiscordApplicationCommand, Command)>(
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
			foreach (DiscordApplicationCommand command in await m_DiscordClient.BulkOverwriteGuildApplicationCommandsAsync(346682476149866497, preCommandMap.Select(kvp => kvp.Value.Item1))) {
				m_CommandMap[command.Name] = preCommandMap[command.Name].Item2;
			}
		}
		
		public Command? GetCommand(string name) {
			bool result = m_CommandMap.TryGetValue(name, out Command? command);
			return command;
		}
	}
}
