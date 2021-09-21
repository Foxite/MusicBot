namespace IkIheMusicBot {
	public enum EncryptMode {
		/// <summary>
		/// Do not encrypt connections.
		/// </summary>
		Disabled,
		
		/// <summary>
		/// Encypt connections normally.
		/// </summary>
		Standard,
			
		/// <summary>
		/// Encrypt connections, but trust the server's certificate no matter what.
		/// </summary>
		Trust,
	}
}
