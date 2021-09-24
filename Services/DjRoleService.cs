using System;
using System.Linq;
using DSharpPlus.Entities;
using Microsoft.Extensions.Options;

namespace IkIheMusicBot {
	public class DjRoleService {
		private readonly DjRoleConfig m_Config;
		
		public DjRoleService(IOptions<DjRoleConfig> config) {
			m_Config = config.Value;
		}

		public bool CheckPermission(DiscordMember member, bool onlyIfRequired) {
			if (m_Config.DjRoleIsRequired || !onlyIfRequired) {
				return member.Roles.Select(role => role.Id).Any(roleId => m_Config.RoleIds.Contains(roleId));
			} else {
				return true;
			}
		}
	}
}
