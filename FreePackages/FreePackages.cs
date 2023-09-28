﻿using System;
using System.Collections.Generic;
using System.Composition;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Plugins.Interfaces;
using Newtonsoft.Json.Linq;
using SteamKit2;

namespace FreePackages {
	[Export(typeof(IPlugin))]
	public sealed class FreePackages : IASF, IBotModules, ISteamPICSChanges, IBotSteamClient, IBotConnection {
		public string Name => nameof(FreePackages);
		public Version Version => typeof(FreePackages).Assembly.GetName().Version ?? new Version("0");
		private static GlobalCache? GlobalCache;

		public Task OnLoaded() {
			ASF.ArchiLogger.LogGenericInfo("Free Packages ASF Plugin by Citrinate");
			return Task.CompletedTask;
		}

		public async Task OnASFInit(IReadOnlyDictionary<string, JToken>? additionalConfigProperties = null) {
			if (GlobalCache == null) {
				GlobalCache = await GlobalCache.CreateOrLoad().ConfigureAwait(false);
			}
		}

		public async Task OnBotInitModules(Bot bot, IReadOnlyDictionary<string, JToken>? additionalConfigProperties = null) {
			if (additionalConfigProperties == null) {
				return;
			}

			bool isEnabled = false;
			uint? packageLimit = null;
			FilterConfig? filter = null;

			foreach (KeyValuePair<string, JToken> configProperty in additionalConfigProperties) {
				switch (configProperty.Key) {
					case "EnableFreePackages" when configProperty.Value.Type == JTokenType.Boolean: {
						bot.ArchiLogger.LogGenericInfo("Enable Free Packages : " + configProperty.Value);
						if (configProperty.Value.ToObject<bool>()) {
							isEnabled = true;
						}
						break;
					}

					case "FreePackagesPerHour" when configProperty.Value.Type == JTokenType.Integer: {
						bot.ArchiLogger.LogGenericInfo("Free Packages Per Hour : " + configProperty.Value);
						packageLimit = configProperty.Value.ToObject<uint>();
						break;
					}

					case "FreePackagesFilter": {
						filter = configProperty.Value.ToObject<FilterConfig>();
						break;
					}
				}
			}
			
			if (isEnabled) {
				await PackageHandler.AddHandler(bot, filter, packageLimit).ConfigureAwait(false);
			}
		}

		public Task<uint> GetPreferredChangeNumberToStartFrom() {
			ASF.ArchiLogger.LogGenericDebug("Change number");
			return Task.FromResult(GlobalCache?.LastChangeNumber ?? 0);
		}

		public async Task OnPICSChanges(uint currentChangeNumber, IReadOnlyDictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData> appChanges, IReadOnlyDictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData> packageChanges) {
			if (GlobalCache == null) {
				throw new InvalidOperationException(nameof(GlobalCache));
			}

			await PackageHandler.OnPICSChanges(currentChangeNumber, appChanges, packageChanges).ConfigureAwait(false);
			GlobalCache.UpdateChangeNumber(currentChangeNumber);
		}

		public Task OnPICSChangesRestart(uint currentChangeNumber) {
			if (GlobalCache == null) {
				throw new InvalidOperationException(nameof(GlobalCache));
			}

			GlobalCache.UpdateChangeNumber(currentChangeNumber);

			return Task.FromResult(0);
		}

		public Task OnBotSteamCallbacksInit(Bot bot, CallbackManager callbackManager) {
			callbackManager.Subscribe<SteamUser.AccountInfoCallback>(callback => OnAccountInfo(bot, callback));

			return Task.CompletedTask;
		}

		public Task<IReadOnlyCollection<ClientMsgHandler>?> OnBotSteamHandlersInit(Bot bot) {
			return Task.FromResult((IReadOnlyCollection<ClientMsgHandler>?) null);
		}

		private static void OnAccountInfo(Bot bot, SteamUser.AccountInfoCallback callback) {
			PackageHandler.OnAccountInfo(bot, callback);
		}

		public async Task OnBotLoggedOn(Bot bot) {
			await PackageHandler.OnBotLoggedOn(bot).ConfigureAwait(false);
		}

		public Task OnBotDisconnected(Bot bot, EResult reason) {
			return Task.FromResult(0);
		}
	}
}
