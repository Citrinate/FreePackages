using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.IPC.Controllers.Api;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using Microsoft.AspNetCore.Mvc;
using SteamKit2;
using Swashbuckle.AspNetCore.Annotations;

namespace FreePackages.IPC {
	[Route("Api/FreePackages", Name = nameof(FreePackages))]
	public sealed class FreePackagesController : ArchiController {
		[HttpGet("{botNames:required}/GetChangesSince/{changeNumber:required}")]
		[SwaggerOperation (Summary = "Request changes for apps and packages since a given change number")]
		[ProducesResponseType(typeof(GenericResponse<SteamApps.PICSChangesCallback>), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public async Task<ActionResult<GenericResponse>> GetChangesSince(string botNames, uint changeNumber, bool showAppChanges = true, bool showPackageChanges = true) {
			if (string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return BadRequest(new GenericResponse(false, string.Format(Strings.BotNotFound, botNames)));
			}

			Bot? bot = bots.FirstOrDefault(static bot => bot.IsConnectedAndLoggedOn);
			if (bot == null) {
				return BadRequest(new GenericResponse(false, Strings.BotNotConnected));
			}

			SteamApps.PICSChangesCallback picsChanges;
			try {
				picsChanges = await bot.SteamApps.PICSGetChangesSince(changeNumber, showAppChanges, showPackageChanges).ToLongRunningTask().ConfigureAwait(false);
			} catch (Exception e) {
				bot.ArchiLogger.LogGenericWarningException(e);

				return BadRequest(new GenericResponse(false, e.Message));
			}

			return Ok(new GenericResponse<SteamApps.PICSChangesCallback>(true, picsChanges));
		}

		[HttpGet("{botNames:required}/GetProductInfo")]
		[SwaggerOperation (Summary = "Request product information for a list of apps or packages")]
		[ProducesResponseType(typeof(GenericResponse<IEnumerable<SteamApps.PICSProductInfoCallback>>), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(byte[]), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public async Task<ActionResult<GenericResponse>> GetProductInfo(string botNames, string? appIDs, string? packageIDs, bool returnFirstRaw = false) {
			if (string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return BadRequest(new GenericResponse(false, string.Format(Strings.BotNotFound, botNames)));
			}

			Bot? bot = bots.FirstOrDefault(static bot => bot.IsConnectedAndLoggedOn);
			if (bot == null) {
				return BadRequest(new GenericResponse(false, Strings.BotNotConnected));
			}

			IEnumerable<SteamApps.PICSProductInfoCallback> productInfos;
			try {
				List<SteamApps.PICSRequest> apps = appIDs == null ? new() : appIDs.Split(",").Select(x => new SteamApps.PICSRequest(uint.Parse(x))).ToList();
				List<SteamApps.PICSRequest> packages = packageIDs == null ? new() : packageIDs.Split(",").Select(x => new SteamApps.PICSRequest(uint.Parse(x), ASF.GlobalDatabase?.PackageAccessTokensReadOnly.GetValueOrDefault(uint.Parse(x), (ulong) 0) ?? 0)).ToList();
				var response = await bot.SteamApps.PICSGetProductInfo(apps, packages).ToLongRunningTask().ConfigureAwait(false);
				if (response.Results == null) {
					return BadRequest(new GenericResponse(false));
				}

				productInfos = response.Results;				
			} catch (Exception e) {
				bot.ArchiLogger.LogGenericWarningException(e);

				return BadRequest(new GenericResponse(false, e.Message));
			}

			if (returnFirstRaw) {
				var results = productInfos.SelectMany(static result => result.Apps.Values).Concat(productInfos.SelectMany(static result => result.Packages.Values));
				if (results.Count() == 0) {
					return File(Array.Empty<byte>(), "text/plain; charset=utf-8");
				}

				try {
					await using var kvMemory = new MemoryStream();
					results.First().KeyValues.SaveToStream(kvMemory, false);
					return File(kvMemory.ToArray(), "text/plain; charset=utf-8");
				} catch (Exception e) {
					bot.ArchiLogger.LogGenericWarningException(e);

					return BadRequest(new GenericResponse(false, e.Message));
				}
			}

			return Ok(new GenericResponse<IEnumerable<SteamApps.PICSProductInfoCallback>>(true, productInfos));
		}

		[HttpGet("{botName:required}/RequestFreeAppLicense")]
		[SwaggerOperation (Summary = "Request a free license for given appids")]
		[ProducesResponseType(typeof(GenericResponse<SteamApps.FreeLicenseCallback>), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public async Task<ActionResult<GenericResponse>> RequestFreeAppLicense(string botName, string appIDs) {
			if (string.IsNullOrEmpty(botName)) {
				throw new ArgumentNullException(nameof(botName));
			}

			Bot? bot = Bot.GetBot(botName);
			if (bot == null) {
				return BadRequest(new GenericResponse(false, string.Format(Strings.BotNotFound, botName)));
			}
			
			if (!bot.IsConnectedAndLoggedOn) {
				return BadRequest(new GenericResponse(false, Strings.BotNotConnected));
			}

			HashSet<uint> apps = new();
			foreach (string appIDString in appIDs.Split(",", StringSplitOptions.RemoveEmptyEntries)) {
				if (!uint.TryParse(appIDString, out uint appID)) {
					return BadRequest(new GenericResponse(false, String.Format(Strings.ErrorParsingObject, nameof(appIDString))));
				}

				apps.Add(appID);
			}

			if (apps.Count == 0) {
				return BadRequest(new GenericResponse(false, String.Format(Strings.ErrorIsEmpty, nameof(appIDs))));
			}

			SteamApps.FreeLicenseCallback response;
			try {
				response = await bot.SteamApps.RequestFreeLicense(apps).ToLongRunningTask().ConfigureAwait(false);			
			} catch (Exception e) {
				bot.ArchiLogger.LogGenericWarningException(e);

				return BadRequest(new GenericResponse(false, e.Message));
			}

			return Ok(new GenericResponse<SteamApps.FreeLicenseCallback>(true, response));
		}

		[HttpGet("{botName:required}/RequestFreeSubLicense")]
		[SwaggerOperation (Summary = "Request a free license for given subid")]
		[ProducesResponseType(typeof(GenericResponse<FreeSubResponse>), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public async Task<ActionResult<GenericResponse>> RequestFreeSubLicense(string botName, uint subID) {
			if (string.IsNullOrEmpty(botName)) {
				throw new ArgumentNullException(nameof(botName));
			}

			Bot? bot = Bot.GetBot(botName);
			if (bot == null) {
				return BadRequest(new GenericResponse(false, string.Format(Strings.BotNotFound, botName)));
			}
			
			if (!bot.IsConnectedAndLoggedOn) {
				return BadRequest(new GenericResponse(false, Strings.BotNotConnected));
			}

			if (PackageQueue.AddFreeLicense == null) {
				return BadRequest(new GenericResponse(false, "Couldn't find ArchiWebHandler.AddFreeLicense method"));
			}

			EResult result;
			EPurchaseResultDetail purchaseResult;
			try {
				var res = (Task<(EResult, EPurchaseResultDetail)>) PackageQueue.AddFreeLicense.Invoke(bot.ArchiWebHandler, new object[]{subID})!;
				await res;
				var a = res.Result;

				(result, purchaseResult) = res.Result;
			} catch (Exception e) {
				bot.ArchiLogger.LogGenericWarningException(e);

				return BadRequest(new GenericResponse(false, e.Message));
			}

			return Ok(new GenericResponse<FreeSubResponse>(true, new FreeSubResponse(result, purchaseResult)));
		}

		[HttpGet("{botName:required}/GetOwnedPackages")]
		[SwaggerOperation (Summary = "Retrieves all packages owned by the given bot")]
		[ProducesResponseType(typeof(GenericResponse<IEnumerable<uint>>), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public ActionResult<GenericResponse> GetOwnedPackages(string botName) {
			if (string.IsNullOrEmpty(botName)) {
				throw new ArgumentNullException(nameof(botName));
			}

			Bot? bot = Bot.GetBot(botName);
			if (bot == null) {
				return BadRequest(new GenericResponse(false, string.Format(Strings.BotNotFound, botName)));
			}

			if (bot.OwnedPackageIDs.Count == 0) {
				return BadRequest(new GenericResponse(false, "No packages found"));
			}

			return Ok(new GenericResponse<IEnumerable<uint>>(true, bot.OwnedPackageIDs.Keys));
		}

		[HttpGet("{botName:required}/GetOwnedApps")]
		[SwaggerOperation (Summary = "Retrieves all apps owned by the given bot")]
		[ProducesResponseType(typeof(GenericResponse<IEnumerable<uint>>), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse<Dictionary<uint, string>>), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public async Task<ActionResult<GenericResponse>> GetOwnedApps(string botName, bool showNames = false) {
			if (string.IsNullOrEmpty(botName)) {
				throw new ArgumentNullException(nameof(botName));
			}

			Bot? bot = Bot.GetBot(botName);
			if (bot == null) {
				return BadRequest(new GenericResponse(false, String.Format(Strings.BotNotFound, botName)));
			}

			if (bot.OwnedPackageIDs.Count == 0) {
				return BadRequest(new GenericResponse(false, "No apps found"));
			}

			if (ASF.GlobalDatabase == null) {
				return BadRequest(new GenericResponse(false, String.Format(Strings.ErrorObjectIsNull, nameof(ASF.GlobalDatabase))));
			}

			var ownedPackageIDs = bot.OwnedPackageIDs.Keys.ToHashSet();
			var ownedAppIDs = ASF.GlobalDatabase!.PackagesDataReadOnly.Where(x => ownedPackageIDs.Contains(x.Key) && x.Value.AppIDs != null).SelectMany(x => x.Value.AppIDs!).ToHashSet().ToList();
			ownedAppIDs.Sort();

			if (showNames) {
				Dictionary<uint, string>? appList;
				try {
					appList = await WebRequest.GetAppList(bot).ConfigureAwait(false);
					if (appList == null) {
						return BadRequest(new GenericResponse(false, "Failed to get app list"));
					}
				} catch (Exception e) {
					return BadRequest(new GenericResponse(false, e.Message));
				}

				return Ok(new GenericResponse<Dictionary<uint, string>>(true, ownedAppIDs.ToDictionary(x => x, x => appList.TryGetValue(x, out string? name) ? name : String.Format("Unknown App {0}", x))));
			}

			return Ok(new GenericResponse<IEnumerable<uint>>(true, ownedAppIDs));
		}

		[Consumes("application/json")]
		[HttpPost("{botNames:required}/QueueLicenses")]
		[SwaggerOperation (Summary = "Adds the provided appids and subids to the given bot's package queue")]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public ActionResult<GenericResponse> QueueLicenses(string botNames, [FromBody] QueueLicensesRequest request) {
			if (string.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);
			if (bots == null || bots.Count == 0) {
				return BadRequest(new GenericResponse(false, String.Format(Strings.BotNotFound, botNames)));
			}

			if (PackageHandler.Handlers.Keys.Union(bots.Select(x => x.BotName)).Count() == 0) {
				return BadRequest(new GenericResponse(false, "Free Packages plugin not enabled"));
			}

			foreach (Bot bot in bots) {
				if (!PackageHandler.Handlers.Keys.Contains(bot.BotName)) {
					continue;
				}

				PackageHandler.Handlers[bot.BotName].AddPackages(request.AppIDs, request.PackageIDs, request.UseFilter);
			}

			return Ok(new GenericResponse(true));
		}
	}
}