using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using SteamKit2;
using SteamKit2.Internal;

namespace FreePackages {
	internal sealed class SteamHandler : ClientMsgHandler {
		internal static ConcurrentDictionary<string, SteamHandler> Handlers = new();

		internal static SteamHandler AddHandler(Bot bot) {
			if (Handlers.ContainsKey(bot.BotName)) {
				Handlers.TryRemove(bot.BotName, out SteamHandler? _);
			}

			SteamHandler handler = new();
			Handlers.TryAdd(bot.BotName, handler);

			return handler;
		}

		public override void HandleMsg(IPacketMsg packetMsg) { }

		public async Task<Dictionary<uint, CPlayer_GetOwnedGames_Response.Game>?> GetOwnedGames(ulong steamID) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
				throw new ArgumentOutOfRangeException(nameof(steamID));
			}

			if (Client == null) {
				throw new InvalidOperationException(nameof(Client));
			}

			if (!Client.IsConnected) {
				return null;
			}

			SteamUnifiedMessages steamUnifiedMessages = Client.GetHandler<SteamUnifiedMessages>() ?? throw new InvalidOperationException(nameof(SteamUnifiedMessages));
			Player unifiedPlayerService = steamUnifiedMessages.CreateService<Player>();

			CPlayer_GetOwnedGames_Request request = new() {
				steamid = steamID,
				include_appinfo = true,
				include_extended_appinfo = true,
				include_free_sub = true,
				include_played_free_games = true,
				skip_unvetted_apps = false
			};

			SteamUnifiedMessages.ServiceMethodResponse<CPlayer_GetOwnedGames_Response> response;

			try {
				response = await unifiedPlayerService.GetOwnedGames(request).ToLongRunningTask().ConfigureAwait(false);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericWarningException(e);

				return null;
			}

			return response.Result == EResult.OK ? response.Body.games.ToDictionary(static game => (uint)game.appid, static game => game) : null;
		}
	}
}
