using System;
using System.Reflection;
using ArchiSteamFarm.Core;

// ASFEnhanced Adapter https://github.com/chr233/ASFEnhanceAdapterDemoPlugin

namespace FreePackages;
internal static class AdapterBridge
{
    /// <summary>
    /// 注册子模块
    /// </summary>
    /// <param name="pluginName">插件名称</param>
    /// <param name="pluginId">插件唯一标识符</param>
    /// <param name="cmdPrefix">命令前缀</param>
    /// <param name="repoName">自动更新仓库</param>
    /// <param name="cmdHandler">命令处理函数</param>
    /// <returns></returns>
    public static bool InitAdapter(string pluginName, string pluginId, string? cmdPrefix, string? repoName, MethodInfo? cmdHandler)
    {
        try
        {
            var adapterEndpoint = Assembly.Load("ASFEnhance").GetType("ASFEnhance._Adapter_.Endpoint");
            var registerModule = adapterEndpoint?.GetMethod("RegisterModule", BindingFlags.Static | BindingFlags.Public);
            var pluinVersion = Assembly.GetExecutingAssembly().GetName().Version;

            if (registerModule != null && adapterEndpoint != null)
            {
                var result = registerModule?.Invoke(null, new object?[] { pluginName, pluginId, cmdPrefix, repoName, pluinVersion, cmdHandler });

                if (result is string str)
                {
                    if (str == pluginName)
                    {
                        return true;
                    }
                    else
                    {
                        ASF.ArchiLogger.LogGenericWarning(str);
                    }
                }
            }
        }
#if DEBUG
        catch (Exception ex)
        {
            ASF.ArchiLogger.LogGenericException(ex, "Community with ASFEnhance failed");
        }
#else
        catch (Exception)
        {
            ASF.ArchiLogger.LogGenericDebug("Community with ASFEnhance failed");
        }
#endif
        return false;
    }
}
