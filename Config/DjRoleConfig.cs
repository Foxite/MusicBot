using System.Collections.Generic;

namespace IkIheMusicBot {
	public class DjRoleConfig {
		public bool DjRoleIsRequired { get; set; } = false;
		public HashSet<ulong> RoleIds { get; set; } = new();
	}
}
