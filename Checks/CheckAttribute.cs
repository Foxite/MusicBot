using System.Threading.Tasks;
using Qmmands;

namespace IkIheMusicBot {
	// Want to make this generic (of CommandContext) but apparently you can't have generic types that derive from Attribute, even if they're abstract
	public abstract class CheckAttribute : Qmmands.CheckAttribute {
		public override ValueTask<CheckResult> CheckAsync(CommandContext context) => CheckAsync((DiscordCommandContext) context);

		protected abstract ValueTask<CheckResult> CheckAsync(DiscordCommandContext context);
	}
}
