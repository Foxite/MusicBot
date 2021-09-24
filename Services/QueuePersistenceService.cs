using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Lavalink;
using Foxite.Common.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IkIheMusicBot {
	public sealed class QueuePersistenceService {
		private readonly NotificationService m_Notifications;
		private readonly Func<QueueDbContext> m_DbContextFactory;
		private readonly ILogger<Program> m_Logger;
		private readonly DiscordClient m_Discord;
		private readonly LavalinkManager m_LavalinkManager;

		public QueuePersistenceService(NotificationService notifications, Func<QueueDbContext> dbContextFactory, ILogger<Program> logger, DiscordClient discord, LavalinkManager lavalinkManager) {
			m_Notifications = notifications;
			m_DbContextFactory = dbContextFactory;
			m_Logger = logger;
			m_Discord = discord;
			m_LavalinkManager = lavalinkManager;
		}
		
		public async Task RestartQueuesAsync() {
			Exception? firstException = null;
			int failCount = 0;
			await using (QueueDbContext dbContext = m_DbContextFactory()) {
				foreach (var savedQueue in dbContext.GuildQueues.Include(queue => queue.Tracks)) {
					try {
						DiscordGuild guild = await m_Discord.GetGuildAsync(savedQueue.DiscordGuildId);
						IReadOnlyList<DiscordChannel> channels = await guild.GetChannelsAsync();
						foreach (GuildQueueTrack savedTrack in savedQueue.Tracks) {
							await m_LavalinkManager.QueueAsync(channels.First(channel => channel.Id == savedQueue.DiscordChannelId), savedTrack.TrackUri.ToString(), LavalinkSearchType.Plain);
						}
						m_LavalinkManager.SetRepeating(guild, savedQueue.Repeating);
					} catch (Exception e) {
						m_Logger.LogError(e, "Failed to restore saved queue for guild {0}", savedQueue.DiscordGuildId);
						failCount++;
						firstException ??= e;
					}
				}
			}

			if (firstException != null) {
				try {
					await m_Notifications.SendNotificationAsync($"Failed to restore saved queues for {failCount} guilds. First exception: {firstException.ToStringDemystified()}");
				} catch (Exception e) {
					m_Logger.LogCritical(e, "Could not send error notification for failed restores");
				}
			}
		}
	}
}
