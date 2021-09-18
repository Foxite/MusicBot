using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Lavalink;
using DSharpPlus.Net;
using IkIheMusicBot.Services;
using Qmmands;

namespace IkIheMusicBot {
	public class MusicModule : ModuleBase<DiscordCommandContext> {
		public LavalinkManager Lavalink { get; set; } = null!;
		
		// [Command("leave"), Description("Leave your voice channel")]
		// public Task Leave() {
		// 	return Lavalink.LeaveAsync(Context.Guild);
		// }

		[Command("play"), Description("Play something")]
		public async Task<CommandResult> Play([Description("Search query or URL")] string search) {
			IReadOnlyList<LavalinkTrack> tracks = await Lavalink.QueueAsync(Context.Guild, ((DiscordMember) Context.User).VoiceState.Channel, search);
			if (tracks.Count == 0) {
				return new TextResult(false, "Added zero tracks");
			} else {
				return new TextResult(true, $"Added {tracks.Count} track{(tracks.Count == 1 ? "" : "s")}");
			}
		}

		[Command("resume"), Description("Resume playback")]
		public Task Resume() {
			return Lavalink.ResumeAsync(Context.Guild);
		}

		[Command("stop", "pause"), Description("Stop playing")]
		public Task Stop() {
			return Lavalink.PauseAsync(Context.Guild);
		}
		
		[Command("skip", "next"), Description("Skip this song")]
		public Task Skip() {
			return Lavalink.SkipAsync(Context.Guild);
		}

		[Command("repeat"), Description("Toggle repeating")]
		public void ToggleRepeat() {
			Lavalink.SetRepeating(Context.Guild, Lavalink.GetRepeating(Context.Guild));
		}

		[Command("np", "nowplaying", "now"), Description("See the current song")]
		public CommandResult NowPlaying() {
			LavalinkTrack track = Lavalink.GetNowPlaying(Context.Guild);
			IReadOnlyList<LavalinkTrack> queue = Lavalink.GetQueue(Context.Guild);
			return new EmbedResult(embedBuilder => {
				embedBuilder.Title = track.Title;
				embedBuilder.Url = track.Uri.ToString();
				embedBuilder.Description = $"{track.Position.ToString()} / {track.Length.ToString()}";
				if (queue.Count >= 1) {
					embedBuilder.AddField("Up next", queue[0].Title, true);
				}
				if (queue.Count >= 2) {
					embedBuilder.AddField("After that", queue[1].Title, true);
				}
			});
		}

		[Command("queue"), Description("See the queue")]
		public CommandResult Queue() {
			var ret = new StringBuilder();
			ret.AppendLine($"Queue for {Context.Channel.Name}");
			foreach (LavalinkTrack track in Lavalink.GetQueue(Context.Guild).Take(20)) { // TODO pagination
				ret.AppendLine($"[{track.Identifier}] {track.Title} ({track.Length.ToString()})");
			}
			return new TextResult(true, ret.ToString());
		}
	}
}
