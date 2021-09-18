using System.Threading.Tasks;
using DSharpPlus.Entities;

namespace IkIheMusicBot {
	public abstract class CommandResult : Qmmands.CommandResult {
		public override bool IsSuccessful { get; }
		
		protected CommandResult(bool isSuccessful) {
			IsSuccessful = isSuccessful;
		}

		public abstract Task HandleAsync(DiscordCommandContext context, DiscordFollowupMessageBuilder messageBuilder);
	}
}
