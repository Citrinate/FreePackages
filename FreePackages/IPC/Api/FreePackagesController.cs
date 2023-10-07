using System;
using System.Collections.Generic;
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
		[HttpGet("{botName:required}/GetChangesSince/{changeNumber:required}")]
		[SwaggerOperation (Summary = "Request changes for apps and packages since a given change number")]
		[ProducesResponseType(typeof(GenericResponse<SteamApps.PICSChangesCallback>), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public async Task<ActionResult<GenericResponse>> GetChangesSince(string botName, uint changeNumber) {
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

			SteamApps.PICSChangesCallback picsChanges;
			try {
				picsChanges = await bot.SteamApps.PICSGetChangesSince(changeNumber, true, true).ToLongRunningTask().ConfigureAwait(false);
			} catch (Exception e) {
				bot.ArchiLogger.LogGenericWarningException(e);

				return BadRequest(new GenericResponse(false, e.Message));
			}

			return Ok(new GenericResponse<SteamApps.PICSChangesCallback>(true, picsChanges));
		}

		[HttpGet("{botName:required}/GetProductInfo")]
		[SwaggerOperation (Summary = "Request product information for a list of apps or packages")]
		[ProducesResponseType(typeof(GenericResponse<IEnumerable<SteamApps.PICSProductInfoCallback>>), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public async Task<ActionResult<GenericResponse>> GetProductInfo(string botName, string? appIDs, string? packageIDs) {
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

			IEnumerable<SteamApps.PICSProductInfoCallback> productInfos;
			try {
				List<SteamApps.PICSRequest> apps = appIDs == null ? new() : appIDs.Split(",").Select(x => new SteamApps.PICSRequest(uint.Parse(x))).ToList();
				List<SteamApps.PICSRequest> packages = packageIDs == null ? new() : packageIDs.Split(",").Select(x => new SteamApps.PICSRequest(uint.Parse(x))).ToList();
				var response = await bot.SteamApps.PICSGetProductInfo(apps, packages).ToLongRunningTask().ConfigureAwait(false);
				if (response.Results == null) {
					return BadRequest(new GenericResponse(false));
				}

				productInfos = response.Results;				
			} catch (Exception e) {
				bot.ArchiLogger.LogGenericWarningException(e);

				return BadRequest(new GenericResponse(false, e.Message));
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
	}
}