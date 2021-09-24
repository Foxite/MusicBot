using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using DSharpPlus.Lavalink;
using IkIheMusicBot.Services;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Crypto.Tls;
using Qmmands;

namespace IkIheMusicBot {
	public class MusicModule : ModuleBase<DiscordCommandContext> {
		public LavalinkManager Lavalink { get; set; } = null!;
		public DjRoleService DjRoleService { get; set; } = null!;
		public IOptions<LocalMediaConfig> LocalMediaConfig { get; set; } = null!;
		
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
		[DjRole(OnlyIfRequired = true)]
		public async Task<CommandResult> Play([Description("Search query or URL")] string search) {
			DiscordChannel? voiceChannel = ((DiscordMember) Context.User).VoiceState?.Channel;
			if (voiceChannel == null) {
				return new TextResult(false, "You need to be in a voice channel for that.");
			}
			
			LavalinkSearchType searchType;
			if (Uri.TryCreate(search, UriKind.Absolute, out _)) {
				if (search.StartsWith("/")) {
					if (DjRoleService.CheckPermission(Context.Member, false)) {
						if (LocalMediaConfig.Value.AllowedPathPrefixes.Any(search.StartsWith)) {
							searchType = LavalinkSearchType.Plain;
						} else {
							return new TextResult(false, "Local file is outside permitted directories");
						}
					} else {
						//return new TextResult(false, "<a:hackerman:585659270767575040>");
						searchType = LavalinkSearchType.Plain;
					}
				} else {
					searchType = LavalinkSearchType.Plain;
				}
			} else {
				searchType = LavalinkSearchType.Youtube;
			}
			IReadOnlyList<LavalinkTrack> tracks = await Lavalink.QueueAsync(voiceChannel, search, searchType);
			if (tracks.Count == 0) {
				return new TextResult(false, "Added zero tracks");
			} else if (tracks.Count == 1) {
				return new TextResult(true, $"Added {tracks[0].Title} ({tracks[0].Length.ToStringSimple()})");
			} else {
				return new TextResult(true, $"Added {tracks.Count} track{(tracks.Count == 1 ? "" : "s")}");
			}
		}

		[Command("resume"), Description("Resume playback")]
		[DjRole(OnlyIfRequired = true)]
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
		[DjRole(OnlyIfRequired = true)]
		public Task Pause() {
			return Lavalink.PauseAsync(Context.Guild);
		}
		
		[Command("skip", "next"), Description("Skip this song")]
		[DjRole(OnlyIfRequired = true)]
		public async Task<CommandResult> Skip() {
			LavalinkTrack? skippedTrack = await Lavalink.SkipAsync(Context.Guild);
			if (skippedTrack == null) {
				return new TextResult(false, "Did not skip any tracks");
			} else {
				return new TextResult(true, "Skipped " + skippedTrack.Title);
			}
		}

		[Command("repeat"), Description("Toggle repeating"), DjRole]
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

					if (track.Uri.IsAbsoluteUri) {
						embedBuilder.Url = track.Uri.ToString();
					}
					
					embedBuilder.Description = $"{track.Position.ToStringSimple()} / {track.Length.ToStringSimple()}";
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
			var queue = Lavalink.GetQueue(Context.Guild);
			foreach (LavalinkTrack track in queue.Take(10)) { // TODO pagination
				ret.AppendLine($"[{track.Identifier}] {track.Title} ({track.Length.ToStringSimple()})");
			}
			if (queue.Count > 10) {
				ret.AppendLine("");
				ret.AppendLine($"+{queue.Count - 10} items");
			}
			return new TextResult(true, ret.ToString());
		}
	}

	public static class TimespanUtil {
		public static string ToStringSimple(this TimeSpan ts) {
			string ret = "";
			if (ts.TotalHours >= 1) {
				ret += ((int) ts.TotalHours) + ":";
			}
			var formatSpecifier = new string('0', ts.TotalHours >= 1 ? 2 : 1);
			ret += $"{ts.Minutes.ToString(formatSpecifier)}:{ts.Seconds:00}";
			return ret;
		}
	}
}
