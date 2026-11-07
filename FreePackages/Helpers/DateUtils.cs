using System;

// Duplicates of https://github.com/SteamRE/SteamKit/blob/master/SteamKit2/SteamKit2/Util/DateUtils.cs, necessary for non-generic ASF support

namespace FreePackages {
	public static class DateUtils
	{
		public static ulong DateTimeToUnixTime(DateTime time)
		{
			return (ulong)new DateTimeOffset( time ).ToUnixTimeSeconds();
		}
	}
}