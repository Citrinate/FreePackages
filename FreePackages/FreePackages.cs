using System;
using System.Collections.Generic;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Plugins.Interfaces;
using SteamKit2;
using System.Text.Json;
using ArchiSteamFarm.Helpers.Json;

namespace FreePackages {
	[Export(typeof(IPlugin))]
	public sealed class FreePackages : IASF, IBotModules, ISteamPICSChanges, IBotSteamClient, IBotConnection, IBotCommand, IGitHubPluginUpdates {
		public string Name => nameof(FreePackages);
		public string RepositoryName => "Citrinate/FreePackages";
		public Version Version => typeof(FreePackages).Assembly.GetName().Version ?? new Version("0");
		internal static GlobalCache? GlobalCache;

		public Task OnLoaded(CancellationToken cancellationToken = default) {
			ASF.ArchiLogger.LogGenericInfo("Free Packages ASF Plugin by Citrinate");

			return Task.CompletedTask;
		}

		public async Task<string?> OnBotCommand(Bot bot, EBotUserAccess access, string message, string[] args, ulong steamID = 0, CancellationToken cancellationToken = default) {
			return await Commands.Response(bot, access, steamID, message, args).ConfigureAwait(false);
		}

		public async Task OnASFInit(IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties = null, CancellationToken cancellationToken = default) {
			if (GlobalCache == null) {
				GlobalCache = await GlobalCache.CreateOrLoad().ConfigureAwait(false);
			}

			CardApps.Update();
			ASFInfo.Update();
		}

		public async Task OnBotInitModules(Bot bot, IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties = null, CancellationToken cancellationToken = default) {
			if (additionalConfigProperties == null) {
				return;
			}

			bool isEnabled = false;
			uint? packageLimit = null;
			bool pauseWhilePlaying = false;
			bool pauseWhileFarming = false;
			List<FilterConfig> filterConfigs = new();

			foreach (KeyValuePair<string, JsonElement> configProperty in additionalConfigProperties) {
				switch (configProperty.Key) {
					case "EnableFreePackages" when (configProperty.Value.ValueKind == JsonValueKind.True || configProperty.Value.ValueKind == JsonValueKind.False): {
						isEnabled = configProperty.Value.GetBoolean();
						bot.ArchiLogger.LogGenericInfo("Enable Free Packages : " + isEnabled.ToString());
						break;
					}
					
					case "PauseFreePackagesWhilePlaying" when (configProperty.Value.ValueKind == JsonValueKind.True || configProperty.Value.ValueKind == JsonValueKind.False): {
						pauseWhilePlaying = configProperty.Value.GetBoolean();
						bot.ArchiLogger.LogGenericInfo("Pause Free Packages While Playing : " + pauseWhilePlaying.ToString());
						break;
					}
					
					case "PauseFreePackagesWhileFarming" when (configProperty.Value.ValueKind == JsonValueKind.True || configProperty.Value.ValueKind == JsonValueKind.False): {
						pauseWhileFarming = configProperty.Value.GetBoolean();
						bot.ArchiLogger.LogGenericInfo("Pause Free Packages While Farming : " + pauseWhileFarming.ToString());
						break;
					}

					case "FreePackagesPerHour" or "FreePackagesLimit" when configProperty.Value.ValueKind == JsonValueKind.Number: {
						packageLimit = configProperty.Value.ToJsonObject<uint>();
						bot.ArchiLogger.LogGenericInfo("Free Packages Limit : " + packageLimit.ToString());
						break;
					}

					case "FreePackagesFilter": {
						FilterConfig? filter = configProperty.Value.ToJsonObject<FilterConfig>();
						if (filter != null) {
							bot.ArchiLogger.LogGenericInfo("Free Packages Filter : " + filter.ToJsonText());
							filterConfigs.Add(filter);
						}
						break;
					}
					
					case "FreePackagesFilters": {
						List<FilterConfig>? filters = configProperty.Value.ToJsonObject<List<FilterConfig>>();
						if (filters != null) {
							bot.ArchiLogger.LogGenericInfo("Free Packages Filters : " + filters.ToJsonText());
							filterConfigs.AddRange(filters);
						}
						break;
					}
				}
			}
			
			if (isEnabled) {
				await PackageHandler.AddHandler(bot, filterConfigs, packageLimit, pauseWhilePlaying, pauseWhileFarming).ConfigureAwait(false);
			}
		}

		public Task<uint> GetPreferredChangeNumberToStartFrom(CancellationToken cancellationToken = default) {
			return Task.FromResult(GlobalCache?.LastChangeNumber ?? 0);
		}

		public Task OnPICSChanges(uint currentChangeNumber, IReadOnlyDictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData> appChanges, IReadOnlyDictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData> packageChanges, CancellationToken cancellationToken = default) {
			PICSHandler.OnPICSChanges(currentChangeNumber, appChanges, packageChanges);
			
			return Task.CompletedTask;
		}

		public async Task OnPICSChangesRestart(uint currentChangeNumber, CancellationToken cancellationToken = default) {
			await PICSHandler.OnPICSRestart(currentChangeNumber).ConfigureAwait(false);
		}

		public Task OnBotSteamCallbacksInit(Bot bot, CallbackManager callbackManager, CancellationToken cancellationToken = default) {
			callbackManager.Subscribe<SteamApps.LicenseListCallback>(callback => OnLicenseList(bot, callback));

			return Task.CompletedTask;
		}

		public Task<IReadOnlyCollection<ClientMsgHandler>?> OnBotSteamHandlersInit(Bot bot, CancellationToken cancellationToken = default) {
			return Task.FromResult<IReadOnlyCollection<ClientMsgHandler>?>(new List<ClientMsgHandler> { SteamHandler.AddHandler(bot) });
		}

		private static void OnLicenseList (Bot bot, SteamApps.LicenseListCallback callback) {
			PackageHandler.OnLicenseList(bot, callback);
		}

		public async Task OnBotLoggedOn(Bot bot, CancellationToken cancellationToken = default) {
			await PackageHandler.OnBotLoggedOn(bot).ConfigureAwait(false);
		}

		public Task OnBotDisconnected(Bot bot, EResult reason, CancellationToken cancellationToken = default) {
			return Task.FromResult(0);
		}
	}
}
