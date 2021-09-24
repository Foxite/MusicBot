using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using DSharpPlus.Lavalink;
using Foxite.Common.Notifications;
using Microsoft.Extensions.Logging;

namespace IkIheMusicBot.Services {
	public class LavalinkManager {
		private readonly ILoggerFactory m_LoggerFactory;
		private readonly NotificationService m_Notifications;
		public LavalinkExtension Lavalink { get; }
		private readonly ConcurrentDictionary<DiscordGuild, LavalinkQueue> m_Queues;

		public LavalinkManager(LavalinkExtension lavalink, ILoggerFactory loggerFactory, NotificationService notifications) {
			m_LoggerFactory = loggerFactory;
			m_Notifications = notifications;
			Lavalink = lavalink;
			m_Queues = new ConcurrentDictionary<DiscordGuild, LavalinkQueue>();
		}

		public async Task<IReadOnlyList<LavalinkTrack>> QueueAsync(DiscordChannel channel, string searchOrUri, LavalinkSearchType searchType) {
			LavalinkQueue queue = m_Queues.GetOrAdd(channel.Guild, _ => {
				LavalinkNodeConnection lnc = Lavalink.GetIdealNodeConnection();
				// Would very much like for this line to be moved out of this lambda and properly awaited
				// It cannot be awaited because it is a parameter to LavalinkQueue, though some ugly restructuring could fix this
				LavalinkGuildConnection gc = lnc.ConnectAsync(channel).GetAwaiter().GetResult();
				return new LavalinkQueue(gc, m_Notifications, m_LoggerFactory.CreateLogger<LavalinkQueue>(), true);
			});
			
			LavalinkGuildConnection gc = queue.GetGuildConnection();
			
			LavalinkLoadResult result;
			if (searchType == LavalinkSearchType.Plain && searchOrUri.StartsWith("/") && searchOrUri.EndsWith(".m3u")) {
				// TODO this really should not be in here, consider making a PR to lavaplayer for local m3u support.
				string[] lines = await File.ReadAllLinesAsync(searchOrUri);
				var tracks = new List<LavalinkTrack>(lines.Length);
				foreach (string line_ in lines) {
					string line = line_;
					if (!Path.IsPathRooted(line)) {
						line = Path.Combine(Path.GetDirectoryName(searchOrUri)!, line);
					}
					LavalinkLoadResult loadResult = await gc.Node.Rest.GetTracksAsync(line, LavalinkSearchType.Plain);
					if (loadResult.LoadResultType == LavalinkLoadResultType.TrackLoaded) {
						tracks.Add(loadResult.Tracks.First());
						await queue.AddToQueueAsync(loadResult.Tracks.First());
					}
				}
				return tracks;
			} else {
				result = await gc.GetTracksAsync(searchOrUri, searchType);
			}

			if (result.LoadResultType == LavalinkLoadResultType.PlaylistLoaded) {
				foreach (LavalinkTrack track in result.Tracks) {
					await queue.AddToQueueAsync(track);
				}
				return result.Tracks.ToList();
			} else if (result.LoadResultType is LavalinkLoadResultType.SearchResult or LavalinkLoadResultType.TrackLoaded) {
				LavalinkTrack track = result.Tracks.First();
				await queue.AddToQueueAsync(track);
				return new[] { track };
			} else if (result.LoadResultType is LavalinkLoadResultType.NoMatches) {
				return Array.Empty<LavalinkTrack>();
			} else /* if (result.LoadResultType is LavalinkLoadResultType.LoadFailed) */ {
				throw new Exception($"Failed to load tracks (severity: {result.Exception.Severity}): {result.Exception.Message}");
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
