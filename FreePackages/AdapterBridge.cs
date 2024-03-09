using System;
using System.Reflection;
using ArchiSteamFarm.Core;

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
		} catch (Exception ex) {
			ASF.ArchiLogger.LogGenericException(ex, "Community with ASFEnhance failed");
		}
		
		return false;
	}
}
