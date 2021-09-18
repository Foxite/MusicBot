using System;
using DSharpPlus;
using DSharpPlus.Entities;
using Qmmands;

namespace IkIheMusicBot {
	public class DiscordCommandContext : CommandContext {
		public DiscordInteraction Interaction { get; }
		public DiscordChannel Channel { get; }
		public DiscordGuild Guild { get; }
		public DiscordUser User { get; }

		public DiscordCommandContext(IServiceProvider isp, DiscordInteraction interaction) : base(isp) {
			Interaction = interaction;
			Channel = interaction.Channel;
			Guild = interaction.Guild;
			User = interaction.User;
		}
	}
}
