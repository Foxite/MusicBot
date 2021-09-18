using System;
using System.Threading.Tasks;
using DSharpPlus.Entities;

namespace IkIheMusicBot {
	public class EmbedResult : CommandResult {
		private readonly Action<DiscordEmbedBuilder> m_EmbedBuilderAction;
		
		public EmbedResult(Action<DiscordEmbedBuilder> embedBuilderAction) : base(true) {
			m_EmbedBuilderAction = embedBuilderAction;
		}
		
		public override Task HandleAsync(DiscordCommandContext context, DiscordFollowupMessageBuilder messageBuilder) {
			var embedBuilder = new DiscordEmbedBuilder();
			m_EmbedBuilderAction(embedBuilder);
			messageBuilder.AddEmbed(embedBuilder.Build());
			return Task.CompletedTask;
		}
	}
}
