using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using DSharpPlus.Lavalink;

namespace IkIheMusicBot.Services {
	public class LavalinkManager {
		public LavalinkExtension Lavalink { get; }
		private readonly ConcurrentDictionary<DiscordGuild, LavalinkQueue> m_Queues;

		public LavalinkManager(LavalinkExtension lavalink) {
			Lavalink = lavalink;
			m_Queues = new ConcurrentDictionary<DiscordGuild, LavalinkQueue>();
		}

		private LavalinkQueue GetLavalinkQueue(DiscordGuild guild) {
			return m_Queues.GetOrAdd(guild, _ => new LavalinkQueue(Lavalink.GetIdealNodeConnection().GetGuildConnection(guild)));
		}

		public async Task<IReadOnlyList<LavalinkTrack>> QueueAsync(DiscordGuild guild, DiscordChannel channel, string searchOrUri, LavalinkSearchType searchType) {
			LavalinkQueue queue = m_Queues.GetOrAdd(channel.Guild, _ => {
				LavalinkNodeConnection lnc = Lavalink.GetIdealNodeConnection();
				// Would very much like for this line to be moved out of this lambda and properly awaited
				LavalinkGuildConnection gc = lnc.ConnectAsync(channel).GetAwaiter().GetResult();
				return new LavalinkQueue(gc);
			});
			
			LavalinkGuildConnection gc = queue.GetGuildConnection();
			LavalinkLoadResult? result = await gc.GetTracksAsync(searchOrUri, searchType);
			
			if (result.LoadResultType == LavalinkLoadResultType.PlaylistLoaded) {
				foreach (LavalinkTrack track in result.Tracks) {
					await GetLavalinkQueue(guild).AddToQueueAsync(track);
				}
				return result.Tracks.ToList();
			} else if (result.LoadResultType is LavalinkLoadResultType.SearchResult or LavalinkLoadResultType.TrackLoaded) {
				LavalinkTrack track = result.Tracks.First();
				await GetLavalinkQueue(guild).AddToQueueAsync(track);
				return new[] { track };
			} else if (result.LoadResultType is LavalinkLoadResultType.NoMatches) {
				return Array.Empty<LavalinkTrack>();
			} else /* if (result.LoadResultType is LavalinkLoadResultType.LoadFailed) */ {
				throw new Exception($"Failed to load tracks {result.Exception.Severity}: {result.Exception.Message}");
			}
		}

		public async Task<bool> PauseAsync(DiscordGuild guild) {
			if (m_Queues.TryGetValue(guild, out LavalinkQueue? queue)) {
				await queue.GetGuildConnection().PauseAsync();
				return true;
			} else {
				return false;
			}
		}
		
		public async Task<LavalinkTrack?> ResumeAsync(DiscordGuild guild) {
			if (m_Queues.TryGetValue(guild, out LavalinkQueue? queue)) {
				await queue.GetGuildConnection().ResumeAsync();
				return queue.GetQueue(1)[0];
			} else {
				return null;
			}
		}
		
		public async Task<bool> LeaveAsync(DiscordGuild guild) {
			if (m_Queues.TryGetValue(guild, out LavalinkQueue? queue)) {
				await queue.GetGuildConnection().DisconnectAsync();
				return true;
			} else {
				return false;
			}
		}

		public Task<LavalinkTrack?> SkipAsync(DiscordGuild guild) {
			if (m_Queues.TryGetValue(guild, out LavalinkQueue? queue)) {
				return queue.SkipCurrentSongAsync();
			} else {
				return Task.FromResult((LavalinkTrack?) null);
			}
		}
		
		public bool SetRepeating(DiscordGuild guild, bool repeating) {
			if (m_Queues.TryGetValue(guild, out LavalinkQueue? queue)) {
				queue.Repeat = repeating;
				return true;
			} else {
				return false;
			}
		}
		
		public bool? GetRepeating(DiscordGuild guild) {
			if (m_Queues.TryGetValue(guild, out LavalinkQueue? queue)) {
				return queue.Repeat;
			} else {
				return null;
			}
		}

		public IReadOnlyList<LavalinkTrack> GetQueue(DiscordGuild guild, int? showFirst = null) {
			if (m_Queues.TryGetValue(guild, out LavalinkQueue? queue)) {
				return queue.GetQueue(showFirst);
			} else {
				return Array.Empty<LavalinkTrack>();
			}
		}

		public DiscordChannel? GetVoiceChannel(DiscordGuild guild) {
			if (m_Queues.TryGetValue(guild, out LavalinkQueue? queue)) {
				return queue.GetGuildConnection().Channel;
			} else {
				return null;
			}
		}
	}
}
