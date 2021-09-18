using System.Threading.Tasks;
using DSharpPlus.Entities;
using Qmmands;

namespace IkIheMusicBot {
	public class TextResult : CommandResult {
		public string Content { get; }

		public TextResult(bool isSuccessful, string content) : base(isSuccessful) {
			Content = content;
		}
		
		public override Task HandleAsync(DiscordCommandContext context, DiscordFollowupMessageBuilder messageBuilder) {
			messageBuilder.Content = Content;
			return Task.CompletedTask;
		}
	}
}
