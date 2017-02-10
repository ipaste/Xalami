﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Xml.Linq;

namespace Xalami.MSBUILD
{
    public static class XalamiXsTaskHelper
    {
        /// <summary>
        /// Creates a folder to hold a templateified project, templateifies the project, 
        /// and returns the files node string, for the eventual .xpt.xml file for this project.
        /// </summary>
        /// <param name="tempProjectFolder"></param>
        /// <param name="projectCsprojPath"></param>
        /// <returns></returns>
        public static string PrepareProjectFolder(string tempProjectFolder, string projectCsprojPath, string projectName)
        {
            if (Directory.Exists(tempProjectFolder))
            {
                Directory.Delete(tempProjectFolder, true);
            }
            string projectFolder = Path.GetDirectoryName(projectCsprojPath);
            CopyProjectFilesToTempFolder(projectFolder, tempProjectFolder);
            ReplaceNamespace(tempProjectFolder, projectCsprojPath);
            return GenerateFilesNode(FileHelper.ReadFile(projectCsprojPath), projectName);
        }

        private static void ReplaceNamespace(string tempFolder, string csprojPath)
        {
            string csprojXml = FileHelper.ReadFile(csprojPath);
            string rootNamespace = GetExistingRootNamespace(csprojXml);
            string androidRootnamespace = $"{rootNamespace}.Android";
            string iosRootnamespace = $"{rootNamespace}.iOS";
            var ext = new List<string> { ".cs", "xaml" };
            var files = Directory.GetFiles(tempFolder, "*.*", SearchOption.AllDirectories).Where(s => ext.Any(e => s.EndsWith(e)));
            foreach(var file in files)
            {
                string text = FileHelper.ReadFile(file);

                // ProjectName is scope-specific, so if we're in a project, we need to replace SolutionName.PlatformName, so we try 
                // that first before just trying to replace the whole namespace in one go
                text = text.Replace(androidRootnamespace, "${ProjectName}"); 
                text = text.Replace(iosRootnamespace, "${ProjectName}");
                text = text.Replace(rootNamespace, "${ProjectName}");
                FileHelper.WriteFile(file, text);
            }
        }

        /// <summary>
        /// Gets the existing root namespace.
        /// </summary>
        /// <param name="csprojxml">The csprojxml.</param>
        /// <returns></returns>
        private static string GetExistingRootNamespace(string csprojxml)
        {
            XDocument xdoc;
            using (StringReader sr = new StringReader(csprojxml))
            {
                xdoc = XDocument.Load(sr, LoadOptions.None);
            }

            XNamespace ns = "http://schemas.microsoft.com/developer/msbuild/2003";
            return xdoc.Descendants(ns + "RootNamespace").FirstOrDefault().Value.Split('.').FirstOrDefault();
        }

        private static void CopyProjectFilesToTempFolder(string projectFolder, string tempFolder)
        {
            FileHelper.DirectoryCopy(projectFolder, tempFolder, true);
        }
        
        private static void CopyEmbeddedFilesToOutput(string targetDir)
        {
            string[] names = Assembly.GetExecutingAssembly().GetManifestResourceNames();

            foreach (var item in names)
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(item))
                {
                    var targetFile = Path.Combine(targetDir, Path.GetFileName(item.Substring(item.LastIndexOf("EmbeddedFiles.") + 14)));

                    using (var fileStream = File.Create(targetFile))
                    {
                        s.Seek(0, SeekOrigin.Begin);
                        s.CopyTo(fileStream);
                    }
                }
            }
        }        
        
        private static string GenerateFilesNode(string csprojXml, string projectTemplateFolderName)
        {
            //Get project items
            List<CsprojItem> files = new List<CsprojItem>();
            XDocument xdoc;
            using (StringReader sr = new StringReader(csprojXml))
            {
                xdoc = XDocument.Load(sr, LoadOptions.None);
            }

            XNamespace ns = "http://schemas.microsoft.com/developer/msbuild/2003";            
            string itemString = String.Empty;
            foreach (var node in xdoc.Descendants(ns + "ItemGroup"))
            {
                //todo: might need more than just the name here. might need DependentUpon info, etc too
                foreach(var item in node.Elements())
                {
                    itemString = item.Attribute("Include").Value;
                    if (!string.IsNullOrEmpty(itemString)
                        && !itemString.Contains("=")
                        && !itemString.Contains(",")
                        && !itemString.Contains(".csproj")
                        && item.Name.LocalName != "Reference")
                    {
                        string dependentString = item.Descendants(ns + "DependentUpon").FirstOrDefault()?.Value;

                        //need the decode here, because @ symbols are stored URL-encoded in csproj files. And those get used in iOS filenames!
                        files.Add(new CsprojItem(HttpUtility.UrlDecode(itemString), dependentString));
                    }
                }
            }
            //end getting project items                           

            //Sort project items so items at the top level directory appear first            
            var tempList = new List<CsprojItem>();
            foreach (var item in files.OrderByDescending(x => x.Path))
            {
                if (!item.Path.Contains(@"\"))
                {
                    tempList.Insert(0, item);
                }
                else
                {
                    tempList.Add(item);
                }
            }
            files = tempList;
            //end sorting project items           

            //Begin serializing csproj items into .xpt.xml items
            string indent = "				";
            StringBuilder filesString = new StringBuilder();
            foreach (var item in files)
            {
                if (item.Path.EndsWith(".png"))
                {
                    // .pngs (and probably any binary file) need to use the <RawFile/> syntax rather than just <File>
                    filesString.AppendLine($"{indent}<RawFile name=\"{item.Path}\" src=\"{projectTemplateFolderName}/{item.Path}\"/>");
                }
                else
                {
                    if (item.Path.EndsWith(".xaml"))
                    {
                        filesString.AppendLine($"{indent}<File name=\"{item.Path}\" BuildAction=\"EmbeddedResource\" src=\"{projectTemplateFolderName}/{item.Path}\" />");
                    }
                    else if (item.Path.EndsWith(".xaml.cs"))
                    {
                        filesString.AppendLine($"{indent}<File name=\"{item.Path}\" AddStandardHeaders=\"True\" src=\"{projectTemplateFolderName}/{item.Path}\" DependsOn=\"{item.DependsOn}\" />");
                    }
                    else
                    {
                        filesString.AppendLine($"{indent}<File name=\"{item.Path}\" DefaultName=\"{Path.GetFileName(item.Path)}\" src=\"{projectTemplateFolderName}/{item.Path}\" />");
                    }
                }
            }
            //end serializing csproj items
                        
            filesString.Insert(0, "<Files>");            
            filesString.AppendLine("            </Files>");

            return filesString.ToString();            
        }
    }
}