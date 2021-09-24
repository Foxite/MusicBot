using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Lavalink;
using DSharpPlus.Lavalink.EventArgs;
using Foxite.Common.Notifications;
using Foxite.Common;
using Microsoft.Extensions.Logging;

namespace IkIheMusicBot.Services {
	public class LavalinkQueue {
		private readonly NotificationService m_Notifications;
		private readonly ILogger<LavalinkQueue> m_Logger;
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

		public LavalinkQueue(LavalinkGuildConnection guildConnection, NotificationService notifications, ILogger<LavalinkQueue> logger, bool autoPause) {
			m_GuildConnection = guildConnection;
			m_Notifications = notifications;
			m_Logger = logger;
			m_Queue = new LinkedList<LavalinkTrack>();
			m_QueueLock = new object();

			m_GuildConnection.TrackException += (_, e) => {
				FormattableString message = $"TrackException event: guild id: {m_GuildConnection.Guild.Id}; error: {e.Error}";
				m_Logger.LogError(message);
				return m_Notifications.SendNotificationAsync(message.ToString());
			};

			m_GuildConnection.TrackStuck += (_, e) => {
				FormattableString message = $"TrackException event: guild id: {m_GuildConnection.Guild.Id}; threshold: {e.ThresholdMilliseconds}";
				m_Logger.LogError(message);
				return m_Notifications.SendNotificationAsync(message.ToString());
			};
			
			// This seems to happen pretty often and is not a cause for concern, the library fixes it automatically
			m_GuildConnection.DiscordWebSocketClosed += (_, e) => {
				FormattableString message = $"DiscordWebSocketClosed event: guild id: {m_GuildConnection.Guild.Id}; code: {e.Code}; reason: {e.Reason}; remote: {e.Remote}";
				m_Logger.LogError(message);
				return m_Notifications.SendNotificationAsync(message.ToString());
			};

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
							} else {
								m_Queue.RemoveFirst();
								return Task.CompletedTask;
							}
						}
					} else {
						m_Logger.LogError("Failed to proceed to next track in guild id {0}; track end reason is {1}", m_GuildConnection.Guild.Id, e.Reason);
						return m_Notifications.SendNotificationAsync($"Failed to proceed to next track; track end reason is {e.Reason}");
					}
				} catch (Exception exception) {
					m_Logger.LogError(exception, "Failed to proceed to next track in guild id {0}", m_GuildConnection.Guild.Id);
					return m_Notifications.SendNotificationAsync($"Failed to proceed to next track: guild id: {m_GuildConnection.Guild.Id}; exception: {exception.ToStringDemystified()}");
				}
			};

			if (autoPause) {
				// This is necessary for 24/7 bots as they stop playing at random intervals, typically 1 or 2 days after you start them.
				// Initially I would restart the bots to fix this but I've found that simply pausing and resuming playback is sufficient.
				// AutoPause will pause playback when nobody is listening and resume it when someone joins.
				m_GuildConnection.Node.Discord.VoiceStateUpdated += (o, e) => {
					if (m_GuildConnection.Channel.Users.All(user => user.Id == o.CurrentUser.Id || user.Id == e.User.Id)) {
						bool wasInChannel = e.Before?.Channel?.Id == m_GuildConnection.Channel.Id;
						bool nowInChannel = e.After?.Channel?.Id == m_GuildConnection.Channel.Id;
						if (wasInChannel && !nowInChannel) {
							return m_GuildConnection.PauseAsync();
						} else {
							return m_GuildConnection.ResumeAsync();
						}
					}
					return Task.CompletedTask;
				};
			}
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
