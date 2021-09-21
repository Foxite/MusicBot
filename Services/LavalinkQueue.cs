using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Lavalink;
using DSharpPlus.Lavalink.EventArgs;

namespace IkIheMusicBot.Services {
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
				try {
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
				} catch (Exception exception) {
					Console.WriteLine("Failed to proceed to next track: " + exception.ToStringDemystified());
					throw;
				}
			};
		}

		public Task AddToQueueAsync(LavalinkTrack track) {
			lock (m_QueueLock) {
				bool playImmediately = m_Queue.Count == 0 && m_GuildConnection.CurrentState.CurrentTrack == null;

				if (track.Title == "Unknown title" && !track.Uri.IsAbsoluteUri) {
					track.GetType().GetProperty(nameof(track.Title))!.SetValue(track, Path.GetFileNameWithoutExtension(track.Uri.ToString()));
				}
				
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
