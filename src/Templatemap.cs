using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CleanArchitecture.CodeGenerator.Helpers;
using CleanArchitecture.CodeGenerator.Models;
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
		private const string _defaultNamespace = "CleanArchitecture.Razor";
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
		

		public static async Task<string> GetTemplateFilePathAsync(Project project, IntellisenseObject classObject, string file,string itemname,string selectFolder)
		{
			var templatefolders =new string[]{
				"Commands\\AcceptChanges",
				"Commands\\Create",
				"Commands\\Delete",
				"Commands\\Update",
				"Commands\\AddEdit",
				"Commands\\Import",
				"DTOs",
				"Caching",
				"EventHandlers",
				"Events",
				"Queries\\Export",
				"Queries\\GetAll",
				"Queries\\Pagination",
				"Pages",
				"Pages\\Components",
				};
			var extension = Path.GetExtension(file).ToLowerInvariant();
			var name = Path.GetFileName(file);
			var safeName = name.StartsWith(".") ? name : Path.GetFileNameWithoutExtension(file);
			var relative = PackageUtilities.MakeRelative(project.GetRootFolder(), Path.GetDirectoryName(file) ?? "");
			var selectRelative = PackageUtilities.MakeRelative(project.GetRootFolder(), selectFolder ?? "");
			string templateFile = null;
			var list = _templateFiles.ToList();

			AddTemplatesFromCurrentFolder(list, Path.GetDirectoryName(file));

			// Look for direct file name matches
			if (list.Any(f => {
				 var pattern = templatefolders.Where(x => relative.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0).First().Replace("\\","\\\\");
				 var result = Regex.IsMatch(f, pattern, RegexOptions.IgnoreCase);
				 return result;
			 
				}) )
			{
				var tmplFile = list.OrderByDescending(x=>x.Length).FirstOrDefault(f => {
					var pattern = templatefolders.Where(x => relative.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0).First().Replace("\\", "\\\\"); ;
					var result = Regex.IsMatch(f, pattern, RegexOptions.IgnoreCase);
					if (result)
					{
						return Path.GetFileNameWithoutExtension(f).Split(new char[] { '.' }, StringSplitOptions.RemoveEmptyEntries).All(x=>name.IndexOf(x, StringComparison.OrdinalIgnoreCase)>=0);
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


			

			var template = await ReplaceTokensAsync(project, classObject, itemname, relative, selectRelative, templateFile);
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

		private static async Task<string> ReplaceTokensAsync(Project project, IntellisenseObject classObject, string name, string relative,string selectRelative, string templateFile)
		{
			if (string.IsNullOrEmpty(templateFile))
			{
				return templateFile;
			}

			var rootNs = project.GetRootNamespace();
			var ns = string.IsNullOrEmpty(rootNs) ? "MyNamespace" : rootNs;
			var selectNs = ns;
			if (!string.IsNullOrEmpty(relative))
			{
				ns += "." + ProjectHelpers.CleanNameSpace(relative);
			}
			if (!string.IsNullOrEmpty(selectRelative))
			{
				selectNs += "." + ProjectHelpers.CleanNameSpace(selectRelative);
				selectNs = selectNs.Remove(selectNs.Length - 1);
			}
			using (var reader = new StreamReader(templateFile))
			{
				var content = await reader.ReadToEndAsync();
				var nameofPlural = ProjectHelpers.Pluralize(name);
				var dtoFieldDefinition = createDtoFieldDefinition(classObject);
				var importFuncExpression = createImportFuncExpression(classObject);
				var templateFieldDefinition = createTemplateFieldDefinition(classObject);
				var exportFuncExpression = createExportFuncExpression(classObject);
				return content.Replace("{rootnamespace}", _defaultNamespace)
					            .Replace("{namespace}", ns)
							    .Replace("{selectns}", selectNs)
								.Replace("{itemname}", name)
								.Replace("{nameofPlural}", nameofPlural)
								.Replace("{dtoFieldDefinition}", dtoFieldDefinition)
								.Replace("{importFuncExpression}", importFuncExpression)
								.Replace("{templateFieldDefinition}", templateFieldDefinition)
								.Replace("{exportFuncExpression}", exportFuncExpression);
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

		private static string createDtoFieldDefinition(IntellisenseObject classObject)
		{
			var output = new StringBuilder();
			foreach(var property in classObject.Properties.Where(x=>x.Type.IsKnownType==true))
			{
				output.Append($"[Description(\"{property.Name}\")]\r\n");
				if (property.Name == "Id")
				{
					output.Append($"public {property.Type.CodeName} {property.Name} {{get;set;}} \r\n");
				}
				else
				{
					output.Append($"    public {property.Type.CodeName}? {property.Name} {{get;set;}} \r\n");
				}
			}
			return output.ToString();
		}
		private static string createImportFuncExpression(IntellisenseObject classObject)
		{
			var output = new StringBuilder();
			foreach (var property in classObject.Properties.Where(x => x.Type.IsKnownType == true))
			{
				if (property.Name == "Id") continue;
				output.Append($"{{ _localizer[_dto.GetMemberDescription(\"{property.Name}\")], (row, item) => item.{property.Name} = row[_localizer[_dto.GetMemberDescription(\"{property.Name}\")]]?.ToString() }}, \r\n");
			}
			return output.ToString();
		}
		private static string createTemplateFieldDefinition(IntellisenseObject classObject)
		{
			var output = new StringBuilder();
			foreach (var property in classObject.Properties.Where(x => x.Type.IsKnownType == true))
			{
				if (property.Name == "Id") continue;
				output.Append($"_localizer[_dto.GetMemberDescription(\"{property.Name}\")], \r\n");
			}
			return output.ToString();
		}
		private static string createExportFuncExpression(IntellisenseObject classObject)
		{
			var output = new StringBuilder();
			foreach (var property in classObject.Properties.Where(x => x.Type.IsKnownType == true))
			{
				output.Append($"{{_localizer[_dto.GetMemberDescription(\"{property.Name}\")],item => item.{property.Name}}}, \r\n");
			}
			return output.ToString();
		}

		private static string createMudTdHeaderDefinition(IntellisenseObject classObject)
		{

		}
	}
}
