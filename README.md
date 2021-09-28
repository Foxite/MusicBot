# MusicBot
Note: This program is not yet suitable for public use, but feel free to try it.

## Before compiling
0. Make sure you have Lavalink running
1. Copy appsettings.json.example to appsettings.json
2. At the very least, fill in DiscordConfiguration and LavalinkConfig.
3. Optionally fill in Notifications, DjRoleConfig, LocalMediaConfig, and StartupConfig.
4. You may also fill in Logging and CommandServiceConfiguration, but it's not very useful. Ignore DatabaseConfig.
5. Create a bot on discord.com/developers and add it to your (testing) guild

When running in Docker it may be better to leave much of appsettings.json blank, and [use an env-file instead](https://docs.microsoft.com/en-us/dotnet/core/extensions/configuration-providers#environment-variable-configuration-provider).

The bot uses slash commands exclusively so much of its interface is self-documenting™.