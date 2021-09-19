using System.Threading.Tasks;
using Qmmands;

namespace IkIheMusicBot {
	public abstract class CheckAttribute : Qmmands.CheckAttribute {
		public override ValueTask<CheckResult> CheckAsync(CommandContext context) => CheckAsync((DiscordCommandContext) context);

		protected abstract ValueTask<CheckResult> CheckAsync(DiscordCommandContext context);
	}
}
