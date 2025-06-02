// ==UserScript==
// @name        Free Packages Importer
// @namespace   https://github.com/Citrinate
// @author      Citrinate
// @description Transfer packages from SteamDB's free packages tool to the ASF Free Packages plugin
// @version     1.0.3
// @match       *://steamdb.info/freepackages/*
// @connect     localhost
// @connect     127.0.0.1
// @connect     *
// @grant       GM_xmlhttpRequest
// @grant       GM_getValue
// @grant       GM_setValue
// @grant       GM_registerMenuCommand
// @homepageURL https://github.com/Citrinate/FreePackages
// @supportURL  https://github.com/Citrinate/FreePackages/issues
// @downloadURL https://raw.githubusercontent.com/Citrinate/FreePackages/main/FreePackagesImporter/code.user.js
// @updateURL   https://raw.githubusercontent.com/Citrinate/FreePackages/main/FreePackagesImporter/code.user.js
// ==/UserScript==

(async function() {
	"use strict";

	//#region Settings
	const SETTING_ASF_SERVER = "SETTING_ASF_SERVER";
	const SETTING_ASF_PORT = "SETTING_ASF_PORT";
	const SETTING_ASF_PASSWORD = "SETTING_ASF_PASSWORD";
	const SETTING_USE_FILTER = "SETTING_USE_FILTER";

	var defaultSettings = {
		SETTING_ASF_SERVER: "http://localhost",
		SETTING_ASF_PORT: "1242",
		SETTING_ASF_PASSWORD: "",
		SETTING_USE_FILTER: true,
	};

	function GetSetting(name) {
		return GM_getValue(name, defaultSettings[name]);
	}

	function SetSetting(name, value) {
		GM_setValue(name, value);
	}

	GM_registerMenuCommand("Set ASF IPC Password", () => {
		const password = prompt("Enter ASF IPC Password", GetSetting(SETTING_ASF_PASSWORD));

		if (password !== null) {
			SetSetting(SETTING_ASF_PASSWORD, password);
			window.location.reload();
		}
	});
	//#endregion

	BuildInterface();
	ShowMessage("Loading...");

	// Get displayed packages
	var freePackages = null;
	const packageRegex = new RegExp("Package ([0-9]+)");

	function UpdatePackages() {
		let newFreePackages = [];
		let packages = document.querySelectorAll(".package");
		for (let i = 0; i < packages.length; i++) {
			let matches = packages[i].innerText.match(packageRegex);
			if (matches) {
				newFreePackages.push(parseInt(matches[1]));
			}
		}

		freePackages = newFreePackages;
		UpdateInterface();
	}

	var observer = new MutationObserver(() => UpdatePackages());
	observer.observe(document.getElementById("freepackages"), { childList: true });
	UpdatePackages();

	// Get bot list
	var bots = null;
	await SendASF("Bot", "", "GET", "ASF").then((newBots) => {
		bots = newBots;
		UpdateInterface();
	}).catch((error) => {
		if (typeof error != "string") {
			console.log(error);
			error = `Failed to connect to ASF.  Please click on "Settings" and verify your server and port.`;
		}

		Finish();
		ShowMessage(error);
	});

	function Finish() {
		observer.disconnect();
	}

	async function AddPackages() {
		Finish();
		ShowMessage("Adding packages...");

		let bot = document.getElementById("js-freepackages-bot-select").value;
		let data = {
			"PackageIDs": freePackages,
			"UseFilter": GetSetting(SETTING_USE_FILTER)
		}

		await SendASF("FreePackages", "QueueLicenses", "POST", bot, data).then(() => {
			ShowMessage("Packages added!");
		}).catch((error) => {
			console.log(error);
			ShowMessage("Failed to add packages.");
		});
	}

	async function SendASF(operation, path, http_method, target_bot, data = {}) {
		let payload = JSON.stringify(data);
		if (http_method == "HEAD" || http_method == "GET") {
			payload = null;
		}
		return new Promise((resolve, reject) => {
			GM_xmlhttpRequest({
				url: `${GetSetting(SETTING_ASF_SERVER)}:${GetSetting(SETTING_ASF_PORT)}/Api/${operation}/${target_bot}/${path}`,
				method: http_method,
				data: payload,
				responseType: "json",
				headers: {
					"Accept": "application/json",
					"Content-Type": "application/json",
					"Authentication": GetSetting(SETTING_ASF_PASSWORD)
				},
				onload: function(response) {
					var success = response?.response?.Success ?? false;
					var message = response?.response?.Message ?? null;
					var result = response?.response?.Result ?? null;

					if (result?.StatusCode == 401) {
						reject(`Missing or incorrect IPC password.  Please click on "Settings" and verify your IPC password.`);
					}

					if (!success) {
						reject(message ?? response);
					}

					resolve(result ?? response);
				},
				onerror: reject,
				ontimeout: reject,
			});
		});
	}

	//#region UI
	function BuildInterface() {
		document.getElementById("freepackages").insertAdjacentHTML("afterend", `
			<div class="panel" style="border-color: #669900;">
				<div class="panel-heading d-flex" style="background-color: #436600; background-image: none; align-items: center;">
					<span class="flex-grow">Add using Free Packages Plugin</span>
					<button id="js-freepackages-settings-button" class="btn btn-sm" style="background-color: #b86795; color: #ddd; border-color: #90426f;"><svg width="16" height="16" viewBox="0 0 924.001 924.001" class="octicon" aria-hidden="true"><path d="M841.36,187.993L736.009,82.64c-6.51-6.51-16.622-7.735-24.499-2.968l-54.938,33.252 c-26.704-14.917-55.296-26.858-85.328-35.375l-15.326-62.326C553.719,6.284,545.702,0,536.496,0h-148.99 c-9.206,0-17.223,6.284-19.421,15.224L352.759,77.55c-30.032,8.517-58.624,20.458-85.328,35.375l-54.938-33.252 c-7.876-4.767-17.989-3.542-24.499,2.968L82.642,187.993c-6.51,6.51-7.735,16.622-2.968,24.498l33.252,54.938 c-14.917,26.704-26.857,55.296-35.375,85.328l-62.326,15.326c-8.94,2.199-15.224,10.216-15.224,19.422v148.99 c0,9.206,6.284,17.223,15.224,19.421l62.326,15.326c8.517,30.032,20.458,58.624,35.375,85.328l-33.252,54.938 c-4.767,7.876-3.542,17.988,2.968,24.498L187.993,841.36c6.51,6.509,16.622,7.734,24.499,2.968l54.938-33.252 c26.704,14.917,55.295,26.856,85.328,35.375l15.326,62.326c2.198,8.939,10.215,15.224,19.421,15.224h148.99 c9.206,0,17.223-6.284,19.421-15.224l15.326-62.326c30.032-8.518,58.624-20.458,85.328-35.375l54.938,33.252 c7.876,4.767,17.989,3.542,24.499-2.968l105.353-105.353c6.51-6.51,7.734-16.622,2.968-24.498l-33.252-54.938 c14.917-26.704,26.856-55.296,35.375-85.328l62.326-15.326C917.716,553.72,924,545.703,924,536.497v-148.99 c0-9.206-6.284-17.223-15.224-19.421L846.45,352.76c-8.518-30.032-20.458-58.624-35.375-85.328l33.252-54.938 C849.095,204.615,847.87,194.502,841.36,187.993z M462.001,670.481c-115.141,0-208.48-93.341-208.48-208.481 c0-115.141,93.34-208.481,208.48-208.481S670.482,346.859,670.482,462C670.482,577.14,577.142,670.481,462.001,670.481z"></path></svg> Settings</button>
				</div>
				<p id="js-freepackages-console" style="display: none; align-items: center;">
					<button id="js-freepackages-add-button" class="btn"><svg width="16" height="16" viewBox="0 0 32 32" class="octicon octicon-copy" aria-hidden="true"><path d="M15.5 29.5c-7.18 0-13-5.82-13-13s5.82-13 13-13 13 5.82 13 13-5.82 13-13 13zM21.938 15.938c0-0.552-0.448-1-1-1h-4v-4c0-0.552-0.447-1-1-1h-1c-0.553 0-1 0.448-1 1v4h-4c-0.553 0-1 0.448-1 1v1c0 0.553 0.447 1 1 1h4v4c0 0.553 0.447 1 1 1h1c0.553 0 1-0.447 1-1v-4h4c0.552 0 1-0.447 1-1v-1z"></path></svg> Add <span id="js-freepackages-count" style="font-weight: 700; display: inline;"></span> packages</button>
					<span style="padding: 0px 8px;"> to </span>
					<select id="js-freepackages-bot-select" style="padding-right: 40px;"></select>
				</p>
				<p id="js-freepackages-message" style="display: none;"></p>
			</div>

			<div id="js-freepackages-settings" style="position: fixed; z-index: 2147483647; top: 0px; right: 0px; bottom: 0px; left: 0px; display: none; justify-content: center; align-items: center; background-color: rgba(0,0,0,.75);">
				<div style="background-color: #213145; padding: 20px 160px 20px 40px; border-radius: 6px;">
					<h2>Settings</h2>
					<dl class="form flattened">
						<dt class="span2">
							<label for="js-freepackages-settings-asf-server">ASF Server</label>
						</dt>
						<dd>
							<input type="text" id="js-freepackages-settings-asf-server" placeholder="${defaultSettings[SETTING_ASF_SERVER]}" value="${GetSetting(SETTING_ASF_SERVER)}"></input>
						</dd>
					</dl>
					<dl class="form flattened">
						<dt class="span2">
							<label for="js-freepackages-settings-asf-port">ASF Port</label>
						</dt>
						<dd>
							<input type="text" id="js-freepackages-settings-asf-port" placeholder="${defaultSettings[SETTING_ASF_PORT]}" value="${GetSetting(SETTING_ASF_PORT)}"></input>
						</dd>
					</dl>
					<dl class="form flattened">
						<dt class="span2">
							<label for="js-freepackages-settings-asf-password">ASF IPC Password</label>
						</dt>
						<dd style="color:var(--muted-color)">
							<div style="color:var(--muted-color)">
								<svg width="16" height="16" viewBox="0 0 16 16" class="octicon octicon-info" aria-hidden="true"><path d="M0 8a8 8 0 1 1 16 0A8 8 0 0 1 0 8Zm8-6.5a6.5 6.5 0 1 0 0 13 6.5 6.5 0 0 0 0-13ZM6.5 7.75A.75.75 0 0 1 7.25 7h1a.75.75 0 0 1 .75.75v2.75h.25a.75.75 0 0 1 0 1.5h-2a.75.75 0 0 1 0-1.5h.25v-2h-.25a.75.75 0 0 1-.75-.75ZM8 6a1 1 0 1 1 0-2 1 1 0 0 1 0 2Z"></path></svg>
								This setting can be configured from your userscript manager's popup menu, found in your browser's extensions toolbar
							</div>
						</dd>
					</dl>
					<dl class="form flattened">
						<dt class="span2">&nbsp;</dt>
						<dd>
							<label style="vertical-align: middle;"><input type="checkbox" id="js-freepackages-settings-filter"  ${GetSetting(SETTING_USE_FILTER) ? "checked" : ""}> Use package filters</label>
						</dd>
					</dl>
					<dl class="form flattened">
						<dt class="span2">&nbsp;</dt>
						<dd>
							<button id="js-freepackages-settings-save" class="btn">Save</button> <button id="js-freepackages-settings-cancel" class="btn">Cancel</button>
						</dd>
					</dl>
				</div>
			</div>
		`);

		// Add packages
		document.getElementById("js-freepackages-add-button").addEventListener("click", function() {
			AddPackages();
		});

		// Open settings
		document.getElementById("js-freepackages-settings-button").addEventListener("click", function() {
			document.getElementById("js-freepackages-settings").style.display = "flex";
		});

		// Close settings
		document.getElementById("js-freepackages-settings-cancel").addEventListener("click", function() {
			document.getElementById("js-freepackages-settings").style.display = "none";

			document.getElementById("js-freepackages-settings-asf-server").value = GetSetting(SETTING_ASF_SERVER);
			document.getElementById("js-freepackages-settings-asf-port").value = GetSetting(SETTING_ASF_PORT);
			document.getElementById("js-freepackages-settings-asf-password").value = GetSetting(SETTING_ASF_PASSWORD);
			document.getElementById("js-freepackages-settings-filter").checked = GetSetting(SETTING_USE_FILTER);
		});

		// Save settings
		document.getElementById("js-freepackages-settings-save").addEventListener("click", function() {
			let asfServer = document.getElementById("js-freepackages-settings-asf-server").value;
			let asfPort = document.getElementById("js-freepackages-settings-asf-port").value;
			let asfPassword = document.getElementById("js-freepackages-settings-asf-password").value;
			let useFilter = document.getElementById("js-freepackages-settings-filter").checked;

			SetSetting(SETTING_ASF_SERVER, asfServer);
			SetSetting(SETTING_ASF_PORT, asfPort);
			SetSetting(SETTING_ASF_PASSWORD, asfPassword);
			SetSetting(SETTING_USE_FILTER, useFilter);

			location.reload();
		});
	}

	function ShowMessage(message) {
		let messageElement = document.getElementById("js-freepackages-message");
		messageElement.innerText = message;
		document.getElementById("js-freepackages-console").style.display = "none";
		messageElement.style.display = "block";
	}

	function UpdateInterface() {
		if (freePackages == null || bots == null) {
			return;
		}

		if (freePackages.length == 0) {
			ShowMessage("There are no packages to add.");

			return;
		}

		document.getElementById("js-freepackages-count").innerText = freePackages.length;

		let pluginEnabled = false;
		let select = document.getElementById("js-freepackages-bot-select");
		select.innerHTML = `<option value="ASF">All bots</option>`;
		for (const i in bots) {
			let bot = bots[i];

			let opt = document.createElement("option");
			opt.value = bot.BotName;
			opt.innerHTML = bot.BotName;
			if (!(bot.BotConfig.EnableFreePackages ?? false)) {
				opt.innerHTML += " (Plugin not enabled)";
				opt.setAttribute("disabled", "");
			} else {
				pluginEnabled = true;
			}

			select.appendChild(opt);
		}

		document.getElementById("js-freepackages-message").style.display = "none";
		document.getElementById("js-freepackages-console").style.display = "flex";

		if (!pluginEnabled) {
			Finish();
			ShowMessage("No bots have the Free Packages plugin enabled.");
		}
	}
	//#endregion
}) ();
