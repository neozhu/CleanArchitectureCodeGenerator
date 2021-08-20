using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CleanArchitecture.CodeGenerator.Helpers;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace CleanArchitecture.CodeGenerator
{
	internal static class TemplateMap
	{
		private static readonly string _folder;
		private static readonly List<string> _templateFiles = new List<string>();
		private const string _defaultExt = ".txt";
		private const string _templateDir = ".templates";

		static TemplateMap()
		{
			var folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			var userProfile = Path.Combine(folder, ".vs", _templateDir);

			if (Directory.Exists(userProfile))
			{
				_templateFiles.AddRange(Directory.GetFiles(userProfile, "*" + _defaultExt, SearchOption.AllDirectories));
			}

			var assembly = Assembly.GetExecutingAssembly().Location;
			_folder = Path.Combine(Path.GetDirectoryName(assembly), "Templates");
			_templateFiles.AddRange(Directory.GetFiles(_folder, "*" + _defaultExt, SearchOption.AllDirectories));
		}
		

		public static async Task<string> GetTemplateFilePathAsync(Project project, string file,string itemname)
		{
			var extension = Path.GetExtension(file).ToLowerInvariant();
			var name = Path.GetFileName(file);
			var safeName = name.StartsWith(".") ? name : Path.GetFileNameWithoutExtension(file);
			var relative = PackageUtilities.MakeRelative(project.GetRootFolder(), Path.GetDirectoryName(file) ?? "");

			string templateFile = null;
			var list = _templateFiles.ToList();

			AddTemplatesFromCurrentFolder(list, Path.GetDirectoryName(file));

			// Look for direct file name matches
			if (list.Any(f => {
				 var pattern = (relative.IndexOf("\\") >0 ) ? (relative.Substring(relative.IndexOf("\\")).Replace("\\", "\\\\")) : relative;
				 var result = Regex.IsMatch(f, pattern, RegexOptions.IgnoreCase);
				 return result;
			 
				}) )
			{
				var tmplFile = list.OrderByDescending(x=>x).FirstOrDefault(f => {
					var pattern = (relative.IndexOf("\\") > 0) ? (relative.Substring(relative.IndexOf("\\")).Replace("\\", "\\\\")) : relative;
					var result = Regex.IsMatch(f, pattern, RegexOptions.IgnoreCase);
					if (result)
					{
						return Path.GetFileNameWithoutExtension(f).Split(new char[] { '.' }, StringSplitOptions.RemoveEmptyEntries).All(x=>name.IndexOf(x, StringComparison.OrdinalIgnoreCase)>0);
					}
					return false;
				});
				templateFile = tmplFile;  //Path.Combine(Path.GetDirectoryName(tmplFile), name + _defaultExt);//GetTemplate(name);
			}

			// Look for file extension matches
			else if (list.Any(f => Path.GetFileName(f).Equals(extension + _defaultExt, StringComparison.OrdinalIgnoreCase)))
			{
				var tmplFile = list.FirstOrDefault(f => Path.GetFileName(f).Equals(extension + _defaultExt, StringComparison.OrdinalIgnoreCase) && File.Exists(f));
				var tmpl = AdjustForSpecific(safeName, extension);
				templateFile = Path.Combine(Path.GetDirectoryName(tmplFile), tmpl + _defaultExt); //GetTemplate(tmpl);
			}

			var template = await ReplaceTokensAsync(project, itemname, relative, templateFile);
			return NormalizeLineEndings(template);
		}

		private static void AddTemplatesFromCurrentFolder(List<string> list, string dir)
		{
			var current = new DirectoryInfo(dir);
			var dynaList = new List<string>();

			while (current != null)
			{
				var tmplDir = Path.Combine(current.FullName, _templateDir);

				if (Directory.Exists(tmplDir))
				{
					dynaList.AddRange(Directory.GetFiles(tmplDir, "*" + _defaultExt, SearchOption.AllDirectories));
				}

				current = current.Parent;
			}

			list.InsertRange(0, dynaList);
		}

		private static async Task<string> ReplaceTokensAsync(Project project, string name, string relative, string templateFile)
		{
			if (string.IsNullOrEmpty(templateFile))
			{
				return templateFile;
			}

			var rootNs = project.GetRootNamespace();
			var ns = string.IsNullOrEmpty(rootNs) ? "MyNamespace" : rootNs;

			if (!string.IsNullOrEmpty(relative))
			{
				ns += "." + ProjectHelpers.CleanNameSpace(relative);
			}

			using (var reader = new StreamReader(templateFile))
			{
				var content = await reader.ReadToEndAsync();
				var nameofPlural = ProjectHelpers.Pluralize(name);
				return content.Replace("{rootnamespace}", "CleanArchitecture.Razor").Replace("{namespace}", ns)
											.Replace("{itemname}", name).Replace("{nameofPlural}", nameofPlural);
			}
		}

		private static string NormalizeLineEndings(string content)
		{
			if (string.IsNullOrEmpty(content))
			{
				return content;
			}

			return Regex.Replace(content, @"\r\n|\n\r|\n|\r", "\r\n");
		}

		private static string AdjustForSpecific(string safeName, string extension)
		{
			if (Regex.IsMatch(safeName, "^I[A-Z].*"))
			{
				return extension += "-interface";
			}

			return extension;
		}
	}
}
