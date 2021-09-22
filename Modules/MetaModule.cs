using DSharpPlus.Lavalink;
using Qmmands;

namespace IkIheMusicBot {
	public class MetaModule {
		[Command("github"), Description("Get a link to the bot's source code")]
		public CommandResult GithubLink() {
			return new TextResult(true, "https://github.com/Foxite/MusicBot");
		}

		[Command("version"), Description("See the bot version")]
		public CommandResult Version() {
			return new TextResult(true, Program.ProgramVersion);
		}
	}
}
