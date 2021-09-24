using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace IkIheMusicBot {
	public class QueueDbContext : DbContext {
		public DbSet<GuildQueue> GuildQueues { get; set; }
	}

	public class GuildQueue {
		[Key]
		public ulong DiscordGuildId { get; set; }
		public ulong DiscordChannelId { get; set; }
		public bool Repeating { get; set; }
		public List<GuildQueueTrack> Tracks { get; set; } = new List<GuildQueueTrack>();
	}

	public class GuildQueueTrack {
		public Guid Id { get; set; }
		public TimeSpan Position { get; set; }
		public Uri TrackUri { get; set; }
	}
}
