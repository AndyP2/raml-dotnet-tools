﻿using System;
using System.IO;
using System.Net.Http;
using Microsoft.VisualStudio.Shell;
using Raml.Parser;

namespace Raml.Common
{
    public static class RamlInfoService
    {
        public static RamlInfo GetRamlInfo(string ramlSource)
        {
            var info = new RamlInfo();

            if (ramlSource.StartsWith("http"))
            {
                Uri uri;
                if (!Uri.TryCreate(ramlSource, UriKind.Absolute, out uri))
                {
                    info.ErrorMessage = "Invalid Url specified: " + uri.AbsoluteUri;
                    ActivityLog.LogError(VisualStudioAutomationHelper.RamlVsToolsActivityLogSource, info.ErrorMessage);
                    return info;
                }

                var absolutePath = uri.AbsoluteUri;
                if (absolutePath.Contains("/"))
                    absolutePath = absolutePath.Substring(0, absolutePath.LastIndexOf("/", StringComparison.InvariantCulture) + 1);

                info.AbsolutePath = absolutePath;

                try
                {
                    info.RamlContents = Downloader.GetContents(uri);
                }
                catch (HttpRequestException rex)
                {
                    var errorMessage = rex.Message;
                    if (rex.InnerException != null)
                        errorMessage += " - " + rex.InnerException.Message;

                    if (errorMessage.Contains("404"))
                        errorMessage = "Url not found, could not load the specified url: " + uri.AbsoluteUri;

                    info.ErrorMessage = errorMessage;

                    ActivityLog.LogError(VisualStudioAutomationHelper.RamlVsToolsActivityLogSource,
                        VisualStudioAutomationHelper.GetExceptionInfo(rex));

                    return info;
                }
                catch (Exception ex)
                {
                    var errorMessage = ex.Message;
                    if (ex.InnerException != null)
                        errorMessage += " - " + ex.InnerException.Message;

                    info.ErrorMessage = "Error when trying to load specified url " + uri.AbsoluteUri + ". " + errorMessage;

                    ActivityLog.LogError(VisualStudioAutomationHelper.RamlVsToolsActivityLogSource,
                        VisualStudioAutomationHelper.GetExceptionInfo(ex));

                    return info;
                }
            }
            else
            {
                if (!File.Exists(ramlSource))
                {
                    info.ErrorMessage = "Error. File " + ramlSource + " does not exist.";
                    return info;
                }

                info.AbsolutePath = Path.GetDirectoryName(ramlSource) + "\\";

                try
                {
                    info.RamlContents = File.ReadAllText(ramlSource);
                }
                catch (Exception ex)
                {
                    var errorMessage = ex.Message;
                    if (ex.InnerException != null)
                        errorMessage += " - " + ex.InnerException.Message;

                    info.ErrorMessage = "Error when trying to read file " + ramlSource + ". " + errorMessage;

                    ActivityLog.LogError(VisualStudioAutomationHelper.RamlVsToolsActivityLogSource,
                        VisualStudioAutomationHelper.GetExceptionInfo(ex));

                    return info;
                }
            }

            var task = new RamlParser().LoadRamlAsync(info.RamlContents);
            task.WaitWithPumping();
            info.RamlDocument = task.Result;

            return info;
        }

    }
}