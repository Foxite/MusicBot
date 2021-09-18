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

		private LavalinkGuildConnection GetGuildConnectionAndConnect(DiscordChannel channel) {
			LavalinkQueue queue = m_Queues.GetOrAdd(channel.Guild, _ => {
				LavalinkNodeConnection lnc = Lavalink.GetIdealNodeConnection();
				LavalinkGuildConnection gc = lnc.ConnectAsync(channel).GetAwaiter().GetResult();
				return new LavalinkQueue(gc);
			});
			return queue.GetGuildConnection();
		}

		private LavalinkGuildConnection GetGuildConnection(DiscordGuild guild) {
			return m_Queues.GetOrAdd(guild, _ => new LavalinkQueue(Lavalink.GetIdealNodeConnection().GetGuildConnection(guild))).GetGuildConnection();
		}

		public async Task<IReadOnlyList<LavalinkTrack>> QueueAsync(DiscordGuild guild, DiscordChannel channel, string search) {
			LavalinkGuildConnection gc = GetGuildConnectionAndConnect(channel);
			LavalinkLoadResult result; 
			if (Uri.TryCreate(search, UriKind.Absolute, out Uri? uri)) {
				result = await gc.GetTracksAsync(uri);
			} else {
				result = await gc.GetTracksAsync(search);
			}
			foreach (LavalinkTrack track in result.Tracks) {
				await m_Queues.GetOrAdd(guild, _ => new LavalinkQueue(gc)).AddToQueueAsync(track);
			}
			return result.Tracks.ToList();
		}

		public Task PauseAsync(DiscordGuild guild) {
			return GetGuildConnection(guild).PauseAsync();
		}
		
		public Task ResumeAsync(DiscordGuild guild) {
			return GetGuildConnection(guild).ResumeAsync();
		}
		
		public Task LeaveAsync(DiscordGuild guild) {
			return GetGuildConnection(guild).DisconnectAsync();
		}

		public Task SkipAsync(DiscordGuild guild) {
			return m_Queues[guild].SkipCurrentSongAsync();
		}
		
		public LavalinkTrack GetNowPlaying(DiscordGuild guild) {
			return GetGuildConnection(guild).CurrentState.CurrentTrack;
		}
		
		public void SetRepeating(DiscordGuild guild, bool repeating) {
			m_Queues[guild].Repeat = repeating;
		}
		
		public bool GetRepeating(DiscordGuild guild) {
			return m_Queues[guild].Repeat;
		}

		public IReadOnlyList<LavalinkTrack> GetQueue(DiscordGuild guild) {
			return m_Queues[guild].GetQueue();
		}
	}

	public class LavalinkQueue {
		private readonly LavalinkGuildConnection m_GuildConnection;
		private readonly LinkedList<LavalinkTrack> m_Queue;
		private readonly object m_QueueLock;

		public bool Repeat { get; set; }

		public LavalinkQueue(LavalinkGuildConnection guildConnection) {
			m_GuildConnection = guildConnection;
			m_Queue = new LinkedList<LavalinkTrack>();
			m_QueueLock = new object();
			
			m_GuildConnection.PlaybackFinished += (_, e) => {
				lock (m_QueueLock) {
					if (e.Reason == TrackEndReason.Finished && m_Queue.First != null) {
						return NextSongAsync();
					} else {
						return Task.CompletedTask;
					}
				}
			};
		}

		public Task AddToQueueAsync(LavalinkTrack track) {
			lock (m_QueueLock) {
				if (m_Queue.Count == 0 && m_GuildConnection.CurrentState.CurrentTrack == null) {
					return m_GuildConnection.PlayAsync(track);
				} else {
					m_Queue.AddLast(track);
					return Task.CompletedTask;
				}
			}
		}

		public Task SkipCurrentSongAsync() {
			return NextSongAsync();
		}

		private Task NextSongAsync() {
			lock (m_QueueLock) {
				if (m_Queue.First != null) {
					LavalinkTrack nextTrack = m_Queue.First.Value;
					m_Queue.RemoveFirst();
					if (Repeat) {
						m_Queue.AddLast(nextTrack);
					}
					return m_GuildConnection.PlayAsync(nextTrack);
				} else {
					return Task.CompletedTask;
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

		public IReadOnlyList<LavalinkTrack> GetQueue() {
			lock (m_QueueLock) {
				return m_Queue.ToList();
			}
		}

		public LavalinkGuildConnection GetGuildConnection() => m_GuildConnection;
	}
}
