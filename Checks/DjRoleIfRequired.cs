using System.Linq;
using System.Threading.Tasks;
using IkIheMusicBot.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Qmmands;

namespace IkIheMusicBot {
	public sealed class DjRole : CheckAttribute {
		public bool OnlyIfRequired { get; set; }
		
		protected override ValueTask<CheckResult> CheckAsync(DiscordCommandContext context) {
			var service = context.Services.GetRequiredService<DjRoleService>();
			if (service.CheckPermission(context.Member, OnlyIfRequired)) {
				return new ValueTask<CheckResult>(CheckResult.Successful);
			} else {
				return new ValueTask<CheckResult>(Failure("DJ role is required."));
			}
		}
	}
}
