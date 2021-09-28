using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using DSharpPlus.Lavalink;
using DSharpPlus.Lavalink.EventArgs;
using Foxite.Common.Notifications;
using Foxite.Common;
using Microsoft.Extensions.Logging;

namespace IkIheMusicBot.Services {
	// Class should probably be renamed to LavalinkGuildManager or something, but whatever.
	public class LavalinkQueue {
		private readonly NotificationService m_Notifications;
		private readonly ILogger<LavalinkQueue> m_Logger;
		private readonly LavalinkGuildConnection m_GuildConnection;
		private readonly LinkedList<LavalinkTrack> m_Queue;
		private readonly Timer m_PauseResumeTimer; // TODO dispose it
		private readonly object m_QueueLock;

		public bool Repeat { get; set; }

		private LavalinkTrack? NextTrack {
			get {
				lock (m_QueueLock) {
					return m_Queue.First?.Next?.Value;
				}
			}
		}

		public LavalinkQueue(LavalinkGuildConnection guildConnection, NotificationService notifications, ILogger<LavalinkQueue> logger) {
			m_GuildConnection = guildConnection;
			m_Notifications = notifications;
			m_Logger = logger;
			m_Queue = new LinkedList<LavalinkTrack>();
			m_QueueLock = new object();

			// This is an experimental mitigation for an issue where the bots go silent at random intervals
			// I've noticed in the logs that this event is logged by Lavalink and a few hours later, somone discovers that the bot went silent and pauses/resumes it to fix it.
			// Which is weird because I don't have the intent necessary to receive this event, and yet it is received anyway. When I join or leave voice, the event is not received.
			// So my hypothesis is that this is some sort of Discord bug and Lavalink does not handle it well, and this is the way to fix it.
			m_GuildConnection.Node.Discord.VoiceStateUpdated += async (o, e) => {
				await m_GuildConnection.PauseAsync();
				await m_GuildConnection.ResumeAsync();
			};
			
			m_PauseResumeTimer = new Timer();
			m_PauseResumeTimer.Elapsed += (o, e) => m_GuildConnection.PauseAsync().ContinueWith(_ => m_GuildConnection.ResumeAsync()).RunSynchronously();
			m_PauseResumeTimer.Interval = TimeSpan.FromHours(6).TotalMilliseconds;
			m_PauseResumeTimer.AutoReset = true;
			m_PauseResumeTimer.Enabled = true;

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
