namespace IkIheMusicBot {
	public class PostgresConfig {
		public string Host { get; set; } = "";
		public int Port { get; set; } = 5432;
		public string Database { get; set; } = "musicbot-data";
		public string Username { get; set; } = "musicbot";
		public string Password { get; set; } = "";
		public EncryptMode Encrypt { get; set; } = EncryptMode.Standard;
	}
}
