﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;

namespace Raml.Common
{
	public class RamlIncludesManager
	{
		private readonly char[] includeDirectiveTrimChars = { ' ', '"', '}', ']', ',' };
		private const string IncludeDirective = "!include";
        private readonly IDictionary<string, Task<string>> downloadFileTasks = new Dictionary<string, Task<string>>();
        private readonly IDictionary<string, string> relativePaths = new Dictionary<string, string>();

	    private HttpClient client;
	    private readonly ICollection<string> includeSources = new Collection<string>();

	    private HttpClient Client
	    {
	        get
	        {
                if(client == null)
                    client = new HttpClient();
	            return client;
	        }
	    }


	    public RamlIncludesManagerResult Manage(string ramlSource, string destinationFolder, bool confirmOverrite = false)
		{
			string path;
			string[] lines;
            var destinationFilePath = GetDestinationFilePath(Path.GetTempPath(), ramlSource);

			if (ramlSource.StartsWith("http"))
			{
                var uri = ParseUri(ramlSource);

			    var result = DownloadFileAndWrite(confirmOverrite, uri, destinationFilePath);
                if(!result.IsSuccessStatusCode)
                    return new RamlIncludesManagerResult(result.StatusCode);

			    path = GetPath(ramlSource, uri);

			    lines = File.ReadAllLines(destinationFilePath);
			}
			else
			{
				path = Path.GetDirectoryName(ramlSource);
				lines = File.ReadAllLines(ramlSource);
			}

			var includedFiles = new Collection<string>();

			// if there are any includes and the folder does not exists, create it
			if (lines.Any(l => l.Contains(IncludeDirective)) && !Directory.Exists(destinationFolder))
				Directory.CreateDirectory(destinationFolder);

			ManageNestedFiles(lines, destinationFolder, includedFiles, path, path, destinationFilePath, confirmOverrite);

			return new RamlIncludesManagerResult(string.Join(Environment.NewLine, lines), includedFiles);
		}

	    private static string GetPath(string ramlSource, Uri uri)
	    {
	        string path;
	        path = uri.AbsolutePath;
	        if (ramlSource.Contains("/"))
	            path = ramlSource.Substring(0, ramlSource.LastIndexOf("/", StringComparison.InvariantCulture) + 1);
	        return path;
	    }

	    private HttpResponseMessage DownloadFileAndWrite(bool confirmOverrite, Uri uri, string destinationFilePath)
	    {
	        var downloadTask = Client.GetAsync(uri);
	        downloadTask.WaitWithPumping();
	        var result = downloadTask.ConfigureAwait(false).GetAwaiter().GetResult();
	        if (!result.IsSuccessStatusCode)
	            return result;

	        var readTask = result.Content.ReadAsStringAsync();
	        readTask.WaitWithPumping();
	        var contents = readTask.ConfigureAwait(false).GetAwaiter().GetResult();
	        WriteFile(destinationFilePath, confirmOverrite, contents);
	        return result;
	    }

	    private static Uri ParseUri(string ramlSource)
	    {
	        Uri uri;
	        if (!Uri.TryCreate(ramlSource, UriKind.Absolute, out uri))
	            throw new UriFormatException("Invalid URL: " + ramlSource);
	        return uri;
	    }

	    private void ManageNestedFiles(IList<string> lines, string destinationFolder, ICollection<string> includedFiles, string path, string relativePath, string writeToFilePath, bool confirmOvewrite)
		{
		    var scopeIncludedFiles = new Collection<string>();
			for (var i = 0; i < lines.Count; i++)
			{
				ManageInclude(lines, destinationFolder, includedFiles, path, relativePath, confirmOvewrite, i, scopeIncludedFiles);
			}

            File.WriteAllText(writeToFilePath, string.Join(Environment.NewLine, lines).Trim());

		    ManageIncludedFiles(destinationFolder, includedFiles, path, relativePath, confirmOvewrite, scopeIncludedFiles);
		}

	    private void ManageInclude(IList<string> lines, string destinationFolder, ICollection<string> includedFiles, string path,
	        string relativePath, bool confirmOvewrite, int i, Collection<string> scopeIncludedFiles)
	    {
	        var line = lines[i];
	        if (!line.Contains(IncludeDirective))
	            return;

	        var includeSource = GetIncludePath(line);

	        var destinationFilePath = GetDestinationFilePath(destinationFolder, includeSource);

	        if (!includeSources.Contains(includeSource))
	        {
	            includeSources.Add(includeSource);

	            if (includedFiles.Contains(destinationFilePath)) // same name but different file
	                destinationFilePath = GetUniqueName(destinationFilePath, includedFiles);

	            if (IsWebSource(path, includeSource))
	            {
	                var fullPathIncludeSource = GetFullWebSource(path, includeSource);
	                DownloadFile(fullPathIncludeSource, destinationFilePath);
	            }
	            else
	            {
	                ManageLocalFile(path, relativePath, confirmOvewrite, includeSource, destinationFilePath);
	            }

	            includedFiles.Add(destinationFilePath);
	            scopeIncludedFiles.Add(destinationFilePath);
	        }

	        // replace old include for new include
	        lines[i] = lines[i].Replace(includeSource, GetPathWithoutDriveLetter(destinationFilePath));
	    }

	    private void ManageLocalFile(string path, string relativePath, bool confirmOvewrite, string includeSource,
	        string destinationFilePath)
	    {
	        var fullPathIncludeSource = includeSource;
	        // if relative does not exist, try with full path
	        if (!File.Exists(includeSource))
	        {
	            fullPathIncludeSource = ResolveFullPath(path, relativePath, includeSource);
	            if (!relativePaths.ContainsKey(destinationFilePath))
	                relativePaths.Add(destinationFilePath, Path.GetDirectoryName(fullPathIncludeSource));
	        }

	        // copy file to dest folder
	        if (File.Exists(destinationFilePath) && confirmOvewrite)
	        {
	            var dialogResult = InstallerServices.ShowConfirmationDialog(Path.GetFileName(destinationFilePath));
	            if (dialogResult == MessageBoxResult.Yes)
	            {
	                if (File.Exists(destinationFilePath))
	                    new FileInfo(destinationFilePath).IsReadOnly = false;

	                File.Copy(fullPathIncludeSource, destinationFilePath, true);
	            }
	        }
	        else
	        {
	            if (File.Exists(destinationFilePath))
	                new FileInfo(destinationFilePath).IsReadOnly = false;

	            File.Copy(fullPathIncludeSource, destinationFilePath, true);
	        }
	    }

	    private void ManageIncludedFiles(string destinationFolder, ICollection<string> includedFiles, string path, string relativePath,
	        bool confirmOvewrite, IEnumerable<string> scopeIncludedFiles)
	    {
	        foreach (var includedFile in scopeIncludedFiles)
	        {
	            if (downloadFileTasks.ContainsKey(includedFile))
	            {
	                downloadFileTasks[includedFile].WaitWithPumping();
	                WriteFile(includedFile, confirmOvewrite,
	                    downloadFileTasks[includedFile].ConfigureAwait(false).GetAwaiter().GetResult());
	            }
	            if (relativePaths.ContainsKey(includedFile))
	                relativePath = relativePaths[includedFile];

	            var nestedFileLines = File.ReadAllLines(includedFile);

	            ManageNestedFiles(nestedFileLines, destinationFolder, includedFiles, path, relativePath, includedFile, confirmOvewrite);
	        }
	    }

	    private string GetUniqueName(string destinationFilePath, ICollection<string> includedFiles)
	    {
	        var fileName = Path.GetFileName(destinationFilePath);
	        var path = Path.GetDirectoryName(destinationFilePath);
	        for (var i = 0; i < 100; i++)
	        {
	            var newPath = Path.Combine(path, fileName + i);
                if (!includedFiles.Contains(newPath))
                    return newPath;
	        }
            throw new InvalidOperationException("Could not get a unique file for the included file " + fileName);
	    }

	    private string GetIncludePath(string line)
	    {
	        var indexOfInclude = line.IndexOf(IncludeDirective, StringComparison.Ordinal);
	        var includeSource = line.Substring(indexOfInclude + IncludeDirective.Length).Trim(includeDirectiveTrimChars);
	        includeSource = includeSource.Replace(Environment.NewLine, string.Empty);
            includeSource = includeSource.Replace("\r\n", string.Empty);
            includeSource = includeSource.Replace("\n", string.Empty);
            includeSource = includeSource.Replace("\r", string.Empty);
	        return includeSource;
	    }

	    private static string ResolveFullPath(string path, string relativePath, string includeSource)
	    {
            // copy values ! DO NOT MODIFY original values !
	        var includeToUse = includeSource;

	        var pathToUse = GoUpIfTwoDots(includeSource, relativePath);
            includeToUse = includeToUse.Replace("../", string.Empty);
            includeToUse = includeToUse.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(pathToUse, includeToUse);
            if (File.Exists(fullPath))
                return fullPath;

            includeToUse = includeSource.Replace("../", string.Empty);
            includeToUse = includeToUse.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(path, includeToUse);
	    }

	    private static string GoUpIfTwoDots(string includeToUse, string pathToUse)
	    {
	        if (!includeToUse.StartsWith("../")) 
                return pathToUse;

	        pathToUse = GoUpOneFolder(pathToUse);
	        includeToUse = RemoveTwoDots(includeToUse);
	        return GoUpIfTwoDots(includeToUse, pathToUse);
	    }

	    private static string RemoveTwoDots(string includeToUse)
	    {
	        includeToUse = includeToUse.Substring(3);
	        return includeToUse;
	    }

	    private static string GoUpOneFolder(string pathToUse)
	    {
	        pathToUse = pathToUse.TrimEnd(Path.DirectorySeparatorChar);
	        pathToUse = pathToUse.Substring(0, pathToUse.LastIndexOf(Path.DirectorySeparatorChar));
	        return pathToUse;
	    }

	    private static string GetPathWithoutDriveLetter(string destinationFilePath)
		{
			return destinationFilePath.Substring(1,1) == ":" ? destinationFilePath.Substring(2) : destinationFilePath;
		}

		private string GetDestinationFilePath(string destinationFolder, string includeSource)
		{
			var filename = GetFileName(includeSource);
			var destinationFilePath = Path.Combine(destinationFolder, filename);
			var doubleDirSeparator = Path.DirectorySeparatorChar.ToString(CultureInfo.InvariantCulture) +
			                         Path.DirectorySeparatorChar.ToString(CultureInfo.InvariantCulture);
			destinationFilePath = destinationFilePath.Replace(doubleDirSeparator, Path.DirectorySeparatorChar.ToString(CultureInfo.InvariantCulture));
			return destinationFilePath;
		}

        private static string GetFileName(string ramlSource)
        {
            var filename = Path.GetFileName(ramlSource);
            if (string.IsNullOrWhiteSpace(filename))
                filename = NetNamingMapper.GetObjectName(ramlSource) + ".raml"; //TODO: check
            return filename;
        }

		private void DownloadFile(string ramlSourceUrl, string destinationFilePath)
		{
            var uri = ParseUri(ramlSourceUrl);
            downloadFileTasks.Add(destinationFilePath, GetContentsAsync(uri));
		}
        
	    private static void WriteFile(string destinationFilePath, bool confirmOvewrite, string contents)
	    {
	        if (File.Exists(destinationFilePath) && confirmOvewrite)
	        {
	            var dialogResult = InstallerServices.ShowConfirmationDialog(Path.GetFileName(destinationFilePath));
	            if (dialogResult == MessageBoxResult.Yes)
	            {
	                if (File.Exists(destinationFilePath))
	                    new FileInfo(destinationFilePath).IsReadOnly = false;
	                File.WriteAllText(destinationFilePath, contents.Trim());
	            }
	        }
	        else
	        {
	            if (File.Exists(destinationFilePath))
	                new FileInfo(destinationFilePath).IsReadOnly = false;
	            File.WriteAllText(destinationFilePath, contents.Trim());
	        }
	    }

		private static string GetFullWebSource(string path, string includeSource)
		{
			if (!includeSource.StartsWith("http"))
				includeSource = path.EndsWith("/") || includeSource.StartsWith("/") ? path + includeSource : path + "/" + includeSource;

			return includeSource;
		}

		private static bool IsWebSource(string path, string includeSource)
		{
			return includeSource.StartsWith("http") || (!string.IsNullOrWhiteSpace(path) && path.StartsWith("http"));
		}

        public Task<string> GetContentsAsync(Uri uri)
        {
            return Client.GetStringAsync(uri);
        }
	}
}