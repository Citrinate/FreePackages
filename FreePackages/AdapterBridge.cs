using System;
using System.Reflection;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;

// ASFEnhanced Adapter https://github.com/chr233/ASFEnhanceAdapterDemoPlugin

namespace FreePackages;
internal static class AdapterBridge {
	public static bool InitAdapter(string pluginName, string pluginId, string? cmdPrefix, string? repoName, MethodInfo? cmdHandler) {
		try {
			var adapterEndpoint = Assembly.Load("ASFEnhance").GetType("ASFEnhance._Adapter_.Endpoint");
			var registerModule = adapterEndpoint?.GetMethod("RegisterModule", BindingFlags.Static | BindingFlags.Public);
			var pluinVersion = Assembly.GetExecutingAssembly().GetName().Version;

			if (registerModule != null && adapterEndpoint != null) {
				var result = registerModule?.Invoke(null, new object?[] { pluginName, pluginId, cmdPrefix, repoName, pluinVersion, cmdHandler });

				if (result is string str) {
					if (str == pluginName) {
						return true;
					} else {
						ASF.ArchiLogger.LogGenericWarning(str);
					}
				}
			}
		} catch (Exception) {
			ASF.ArchiLogger.LogGenericDebug("Could not find ASFEnhance plugin");
		}
		
		return false;
	}

	internal static string? Response(Bot bot, EAccess access, ulong steamID, string message, string[] args) {
		// ASFEnhance wants to intercept commands meant for this plugin, for the purpose of it's DisabledCmds config setting.
		// Seems buggy though: https://github.com/Citrinate/FreePackages/issues/28
		// Therefore I'm feeding it this dummy response function, as ASFEnhance requires that cmdHandler not be null.
		// This disables DisabledCmds support, but should not effect PLUGINSUPDATE command support
		
		return null;
	}
}
