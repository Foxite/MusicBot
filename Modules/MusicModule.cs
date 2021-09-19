using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using DSharpPlus.Lavalink;
using IkIheMusicBot.Services;
using Qmmands;

namespace IkIheMusicBot {
	public class MusicModule : ModuleBase<DiscordCommandContext> {
		public LavalinkManager Lavalink { get; set; } = null!;
		
		// [Command("leave"), Description("Leave your voice channel")]
		// public Task Leave() {
		// 	return Lavalink.LeaveAsync(Context.Guild);
		// }

		// TODO move to util class
		private static async Task<bool> LocalFileExistsAndCanRead(string path) {
			try {
				await File.OpenRead(path).DisposeAsync();
				return true;
			} catch (Exception) {
				return false;
			}
		}

		[Command("play"), Description("Play something")]
		public async Task<CommandResult> Play([Description("Search query or URL")] string search) {
			LavalinkSearchType searchType;
			if (Uri.TryCreate(search, UriKind.Absolute, out _)) {
				searchType = LavalinkSearchType.Plain;
			} else if (await LocalFileExistsAndCanRead(search)) {
				if (search.StartsWith("/mnt/data/Music/")) {
					searchType = LavalinkSearchType.Plain;
				} else {
					return new TextResult(false, "<a:aPES_Hacker:513527552976093204>");
				}
			} else {
				searchType = LavalinkSearchType.Youtube;
			}
			IReadOnlyList<LavalinkTrack> tracks = await Lavalink.QueueAsync(Context.Guild, ((DiscordMember) Context.User).VoiceState.Channel, search, searchType);
			if (tracks.Count == 0) {
				return new TextResult(false, "Added zero tracks");
			} else if (tracks.Count == 1) {
				return new TextResult(true, $"Added {tracks[0].Title} ({tracks[0].Length.ToString()})");
			} else {
				return new TextResult(true, $"Added {tracks.Count} track{(tracks.Count == 1 ? "" : "s")}");
			}
		}

		[Command("resume"), Description("Resume playback")]
		public async Task<CommandResult> Resume() {
			LavalinkTrack? result = await Lavalink.ResumeAsync(Context.Guild);
			if (result != null) {
				return new TextResult(true, "Resumed " + result.Title);
			} else {
				DiscordChannel? channel = Lavalink.GetVoiceChannel(Context.Guild);
				if (channel != null) {
					return new TextResult(true, "Nothing is playing in " + channel.Name);
				} else {
					return new TextResult(false, "Nothing is playing in " + Context.Guild.Name);
				}
			}
		}

		[Command("pause"), Description("Pause playback")]
		public Task Pause() {
			return Lavalink.PauseAsync(Context.Guild);
		}
		
		[Command("skip", "next"), Description("Skip this song")]
		public async Task<CommandResult> Skip() {
			LavalinkTrack? skippedTrack = await Lavalink.SkipAsync(Context.Guild);
			if (skippedTrack == null) {
				return new TextResult(false, "Did not skip any tracks");
			} else {
				return new TextResult(true, "Skipped " + skippedTrack.Title);
			}
		}

		[Command("repeat"), Description("Toggle repeating")]
		public CommandResult ToggleRepeat() {
			bool? repeating = Lavalink.GetRepeating(Context.Guild);
			if (repeating.HasValue) {
				Lavalink.SetRepeating(Context.Guild, !repeating.Value);
				return new TextResult(true, $"Now {(!repeating.Value ? "" : "not ")}repeating in {Lavalink.GetVoiceChannel(Context.Guild)!.Name}");
			} else {
				return new TextResult(false, "Nothing is playing in " + Context.Guild.Name);
			}
		}

		[Command("np", "nowplaying", "now"), Description("See the current song")]
		public CommandResult NowPlaying() {
			IReadOnlyList<LavalinkTrack> queue = Lavalink.GetQueue(Context.Guild, 3);
			if (queue.Count == 0) {
				DiscordChannel? channel = Lavalink.GetVoiceChannel(Context.Guild);
				if (channel != null) {
					return new TextResult(true, "Nothing is playing in " + channel.Name);
				} else {
					return new TextResult(false, "Nothing is playing in " + Context.Guild.Name);
				}
			} else {
				LavalinkTrack track = queue[0];
				return new EmbedResult(embedBuilder => {
					embedBuilder.Title = track.Title;
					embedBuilder.Url = track.Uri.ToString();
					embedBuilder.Description = $"{track.Position.ToString()} / {track.Length.ToString()}";
					if (queue.Count >= 2) {
						embedBuilder.AddField("Up next", queue[1].Title, true);
					}
					if (queue.Count >= 3) {
						embedBuilder.AddField("After that", queue[2].Title, true);
					}
				});
			}
		}

		[Command("queue"), Description("See the queue")]
		public CommandResult Queue() {
			var ret = new StringBuilder();
			ret.AppendLine($"Queue for {Context.Channel.Name}");
			foreach (LavalinkTrack track in Lavalink.GetQueue(Context.Guild, 20)) { // TODO pagination
				ret.AppendLine($"[{track.Identifier}] {track.Title} ({track.Length.ToString()})");
			}
			return new TextResult(true, ret.ToString());
		}
	}
}
