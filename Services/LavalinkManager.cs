using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using DSharpPlus.Lavalink;
using DSharpPlus.Lavalink.EventArgs;

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

	public class LavalinkQueue {
		private readonly LavalinkGuildConnection m_GuildConnection;
		private readonly LinkedList<LavalinkTrack> m_Queue;
		private readonly object m_QueueLock;

		public bool Repeat { get; set; }

		private LavalinkTrack? NextTrack {
			get {
				lock (m_QueueLock) {
					return m_Queue.First?.Next?.Value;
				}
			}
		}

		public LavalinkQueue(LavalinkGuildConnection guildConnection) {
			m_GuildConnection = guildConnection;
			m_Queue = new LinkedList<LavalinkTrack>();
			m_QueueLock = new object();

			m_GuildConnection.PlaybackFinished += (_, e) => {
				if (e.Reason == TrackEndReason.Finished) {
					lock (m_QueueLock) {
						if (NextTrack != null) {
							if (Repeat) {
								m_Queue.AddLast(m_Queue.First!.Value);
							}
							m_Queue.RemoveFirst();
							return NextSongAsync();
						}
					}
				}
				return Task.CompletedTask;
			};
		}

		public Task AddToQueueAsync(LavalinkTrack track) {
			lock (m_QueueLock) {
				bool playImmediately = m_Queue.Count == 0 && m_GuildConnection.CurrentState.CurrentTrack == null;
				m_Queue.AddLast(track);
				if (playImmediately) {
					return NextSongAsync();
				} else {
					return Task.CompletedTask;
				}
			}
		}

		public Task<LavalinkTrack?> SkipCurrentSongAsync() {
			lock (m_QueueLock) {
				LavalinkTrack? result = m_Queue.First?.Value;
				if (result == null) {
					return Task.FromResult((LavalinkTrack?) null);
				}
				Task ret;
				if (NextTrack != null) {
					ret = NextSongAsync();
				} else {
					ret = m_GuildConnection.StopAsync();
				}
				return ret.ContinueWith(_ => (LavalinkTrack?) result);
			}
		}

		// Note: if this function ever becomes async you need to make sure that calling functions handles their lock properly
		private Task NextSongAsync() {
			lock (m_QueueLock) {
				if (m_Queue.First == null) {
					return Task.CompletedTask;
				} else if (m_GuildConnection.CurrentState.CurrentTrack == null) {
					return m_GuildConnection.PlayAsync(m_Queue.First.Value);
				} else if (NextTrack != null) {
					Task playTask = m_GuildConnection.PlayAsync(NextTrack);
					if (Repeat) {
						m_Queue.AddLast(m_Queue.First.Value);
					}
					m_Queue.RemoveFirst();
					return playTask;
				} else { // if (NextTrack == null)
					if (Repeat) {
						return m_GuildConnection.SeekAsync(TimeSpan.Zero);
					} else {
						m_Queue.RemoveFirst();
						return m_GuildConnection.StopAsync();
					}
				}
			}
		}

		public bool RemoveQueueItem(string identifier) {
			lock (m_QueueLock) {
				LinkedListNode<LavalinkTrack>? node = null;
				foreach (var item in m_Queue.GetNodes()) {
					if (item.Value.Identifier == identifier) {
						node = item;
					}
				}
				if (node == null) {
					return false;
				} else {
					m_Queue.Remove(node);
					return true;
				}
			}
		}

		public IReadOnlyList<LavalinkTrack> GetQueue(int? showFirst = null) {
			lock (m_QueueLock) {
				IEnumerable<LavalinkTrack> ret = m_Queue;
				if (showFirst.HasValue) {
					ret = ret.Take(showFirst.Value);
				}
				return ret.ToList();
			}
		}

		public LavalinkGuildConnection GetGuildConnection() => m_GuildConnection;
	}
}
