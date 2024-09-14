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


		public static async Task<string> GetTemplateFilePathAsync(Project project, IntellisenseObject classObject, string file, string itemname, string selectFolder, IEnumerable<IntellisenseObject> objectlist = null)
		{
			var templatefolders = new string[]{
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
				"Specification",
				"Queries\\Export",
				"Queries\\GetAll",
				"Queries\\GetById",
				"Queries\\Pagination",
				"Pages",
				"Pages\\Components",
				"Persistence\\Configurations",
				"PermissionSet",
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
				var pattern = templatefolders.Where(x => relative.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0).First().Replace("\\", "\\\\");
				var result = Regex.IsMatch(f, pattern, RegexOptions.IgnoreCase);
				return result;

			}))
			{
				var tmplFile = list.OrderByDescending(x => x.Length).FirstOrDefault(f => {
					var pattern = templatefolders.Where(x => relative.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0).First().Replace("\\", "\\\\"); ;
					var result = Regex.IsMatch(f, pattern, RegexOptions.IgnoreCase);
					if (result)
					{
						return Path.GetFileNameWithoutExtension(f).Split(new char[] { '.' }, StringSplitOptions.RemoveEmptyEntries).All(x => name.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);
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
			var template = await ReplaceTokensAsync(project, classObject, itemname, relative, selectRelative, templateFile, objectlist);
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

		private static async Task<string> ReplaceTokensAsync(Project project, IntellisenseObject classObject, string name, string relative, string selectRelative, string templateFile, IEnumerable<IntellisenseObject> objectlist = null)
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
				var dtoFieldDefinition = createDtoFieldDefinition(classObject, objectlist);
				var importFuncExpression = createImportFuncExpression(classObject, objectlist);
				var templateFieldDefinition = createTemplateFieldDefinition(classObject);
				var exportFuncExpression = createExportFuncExpression(classObject, objectlist);
				var mudTdDefinition = createMudTdDefinition(classObject);
				var mudTdHeaderDefinition = createMudTdHeaderDefinition(classObject, objectlist);
				var mudFormFieldDefinition = createMudFormFieldDefinition(classObject, objectlist);
				var fieldAssignmentDefinition = createFieldAssignmentDefinition(classObject);
				var entityTypeBuilderConfirmation = createEntityTypeBuilderConfirmation(classObject, objectlist);
				var commandValidatorRuleFor = createComandValidatorRuleFor(classObject, objectlist);
				var replacements = new Dictionary<string, string>
				{
					{ "rootnamespace", _defaultNamespace },
					{ "namespace", ns },
					{ "selectns", selectNs },
					{ "itemname", name },
					{ "nameofPlural", nameofPlural },
					{ "dtoFieldDefinition", dtoFieldDefinition },
					{ "fieldAssignmentDefinition", fieldAssignmentDefinition },
					{ "importFuncExpression", importFuncExpression },
					{ "templateFieldDefinition", templateFieldDefinition },
					{ "exportFuncExpression", exportFuncExpression },
					{ "mudTdDefinition", mudTdDefinition },
					{ "mudTdHeaderDefinition", mudTdHeaderDefinition },
					{ "mudFormFieldDefinition", mudFormFieldDefinition },
					{ "entityTypeBuilderConfirmation", entityTypeBuilderConfirmation },
					{ "commandValidatorRuleFor", commandValidatorRuleFor }
				};

				string result = Regex.Replace(content, @"\{(\w+)\}", match => {
					string key = match.Groups[1].Value;
					return replacements.TryGetValue(key, out var value) ? value : match.Value;
				});
				return result;

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
		private static string splitCamelCase(string str)
		{
			// Define the regular expression to split the CamelCase string
			var r = new Regex(@"(?<=[A-Z])(?=[A-Z][a-z]) |  
								(?<=[^A-Z])(?=[A-Z]) |       
								(?<=[A-Za-z])(?=[^A-Za-z])",
				RegexOptions.IgnorePatternWhitespace); // Allows formatting with spaces for better readability

			// Use the regular expression to replace matches with a space
			var result = r.Replace(str, " ");

			// If the result is not empty, proceed to format it
			if (!string.IsNullOrEmpty(result))
			{
				// Convert the entire string to lowercase first
				result = result.ToLower();

				// Then capitalize the first character of the string to ensure the first word is properly capitalized
				result = char.ToUpper(result[0]) + result.Substring(1);
			}

			return result;
		}

		public const string PRIMARYKEY = "Id";
		private static string createDtoFieldDefinition(IntellisenseObject classObject, IEnumerable<IntellisenseObject> objectlist = null)
		{
			var output = new StringBuilder();
			foreach (var property in classObject.Properties.Where(x => x.Type.IsDictionary == false))
			{
				output.Append($"    [Description(\"{splitCamelCase(property.Name)}\")]\r\n");
				if (property.Name == PRIMARYKEY)
				{
					output.Append($"    public {property.Type.CodeName} {property.Name} {{get;set;}} \r\n");
				}
				else
				{
					switch (property.Type.CodeName)
					{
						case "string":
						case "string?":
							if (property.Type.IsArray)
							{
								output.Append($"    public {property.Type.CodeName}[]{(property.Type.IsOptional ? "?" : "")} {property.Name} {{get;set;}} \r\n");
							}
							else if (property.Type.IsDictionary)
							{
								output.Append($"    public Dictionary<{property.Type.CodeName},{property.Type.CodeName}>{(property.Type.IsOptional ? "?" : "")} {property.Name} {{get;set;}} \r\n");
							}
							else
							{

								output.Append($"    public {property.Type.CodeName}{(property.Name.Equals("Name") ? "" : "?")} {property.Name} {{get;set;}} \r\n");
							}
							break;

						case "System.DateTime?":
							output.Append($"    public DateTime? {property.Name} {{get;set;}} \r\n");
							break;
						case "System.DateTime":
							output.Append($"    public DateTime {property.Name} {{get;set;}} \r\n");
							break;
						case "System.TimeSpan?":
							output.Append($"    public TimeSpan? {property.Name} {{get;set;}} \r\n");
							break;
						case "System.TimeSpan":
							output.Append($"    public TimeSpan {property.Name} {{get;set;}} \r\n");
							break;
						case "System.DateTimeOffset":
							output.Append($"    public DateTimeOffset {property.Name} {{get;set;}} \r\n");
							break;
						case "System.DateTimeOffset?":
							output.Append($"    public DateTimeOffset? {property.Name} {{get;set;}} \r\n");
							break;
						case "System.Guid":
							output.Append($"    public Guid {property.Name} {{get;set;}} \r\n");
							break;
						case "System.Guid?":
							output.Append($"    public Guid? {property.Name} {{get;set;}} \r\n");
							break;
						case "bool?":
						case "bool":
						case "byte?":
						case "byte":
						case "char?":
						case "char":
						case "float?":
						case "float":
						case "decimal?":
						case "decimal":
						case "int?":
						case "int":
						case "double?":
						case "double":
							output.Append($"    public {property.Type.CodeName}{(property.Type.IsArray ? "[]" : "")} {property.Name} {{get;set;}} \r\n");
							break;
						default:
							if (objectlist != null && objectlist.Any(x => x.FullName.Equals(property.Type.CodeName)))
							{
								var complexType = property.Type.CodeName.Split('.').Last();
								var relatedObject = objectlist.First(x => x.FullName.Equals(property.Type.CodeName));
								if (relatedObject.IsEnum)
								{
									output.Append($"    public {complexType}? {property.Name} {{get;set;}} \r\n");
								}
								else
								{
									complexType = complexType + "Dto";
									if (property.Type.IsArray)
									{
										complexType = $"List<{complexType}Dto>?";
									}
									output.Append($"    public {complexType} {property.Name} {{get;set;}} \r\n");
								}
							}
							else
							{
								if (property.Name.Equals("Tenant"))
								{
									output.Append($"    public TenantDto? {property.Name} {{get;set;}} \r\n");
								}
							}
							break;
					}
				}
			}


			if (classObject.BaseName.Equals("OwnerPropertyEntity"))
			{
				output.Append($"    [Description(\"Owner\")] public ApplicationUserDto? Owner {{ get; set; }} \r\n");
				output.Append($"    [Description(\"Last modifier\")] public ApplicationUserDto? LastModifier {{ get; set; }} \r\n");
			}
			return output.ToString();
		}
		private static string createImportFuncExpression(IntellisenseObject classObject, IEnumerable<IntellisenseObject> objectlist = null)
		{
			var output = new StringBuilder();
			foreach (var property in classObject.Properties.Where(x => !x.Type.IsDictionary && !x.Type.IsArray))
			{
				if (property.Name == PRIMARYKEY) continue;

				if (property.Type.CodeName.StartsWith("bool"))
				{
					output.Append($"{{ _localizer[_dto.GetMemberDescription(x=>x.{property.Name})], (row, item) => item.{property.Name} =Convert.ToBoolean(row[_localizer[_dto.GetMemberDescription(x=>x.{property.Name})]]) }}, \r\n");
				}
				else if (property.Type.CodeName.StartsWith("System.DateTime"))
				{
					output.Append($"{{ _localizer[_dto.GetMemberDescription(x=>x.{property.Name})], (row, item) => item.{property.Name} =DateTime.Parse(row[_localizer[_dto.GetMemberDescription(x=>x.{property.Name})]].ToString()) }}, \r\n");
				}
				else if (property.Type.CodeName.StartsWith("int"))
				{
					output.Append($"{{ _localizer[_dto.GetMemberDescription(x=>x.{property.Name})], (row, item) => item.{property.Name} =Convert.ToInt32(row[_localizer[_dto.GetMemberDescription(x=>x.{property.Name})]]) }}, \r\n");
				}
				else if (property.Type.CodeName.StartsWith("decimal") || property.Type.CodeName.StartsWith("float"))
				{
					output.Append($"{{ _localizer[_dto.GetMemberDescription(x=>x.{property.Name})], (row, item) => item.{property.Name} =Convert.ToDecimal(row[_localizer[_dto.GetMemberDescription(x=>x.{property.Name})]]) }}, \r\n");
				}
				else
				{
					if (property.Type.IsKnownType)
					{
						output.Append($"{{ _localizer[_dto.GetMemberDescription(x=>x.{property.Name})], (row, item) => item.{property.Name} = row[_localizer[_dto.GetMemberDescription(x=>x.{property.Name})]].ToString() }}, \r\n");
					}
					else
					{
						var relatedObject = objectlist.FirstOrDefault(x => x.FullName.Equals(property.Type.CodeName) && x.IsEnum);
						if (relatedObject != null)
						{
							var enumType = property.Type.CodeName.Split('.').Last();
							output.Append($"{{ _localizer[_dto.GetMemberDescription(x=>x.{property.Name})], (row, item) => item.{property.Name} = Enum.Parse<{enumType}>(row[_localizer[_dto.GetMemberDescription(x=>x.{property.Name})]].ToString()) }}, \r\n");
						}
					}

				}
			}
			return output.ToString();
		}
		private static string createTemplateFieldDefinition(IntellisenseObject classObject)
		{
			var output = new StringBuilder();
			foreach (var property in classObject.Properties.Where(x => x.Type.IsKnownType == true && !x.Type.IsDictionary && !x.Type.IsArray))
			{
				if (property.Name == PRIMARYKEY) continue;
				output.Append($"_localizer[_dto.GetMemberDescription(x=>x.{property.Name})], \r\n");
			}
			return output.ToString();
		}
		private static string createExportFuncExpression(IntellisenseObject classObject, IEnumerable<IntellisenseObject> objectlist = null)
		{
			var output = new StringBuilder();
			foreach (var property in classObject.Properties.Where(x => !x.Type.IsDictionary && !x.Type.IsArray))
			{
				if (property.Type.IsKnownType)
				{
					output.Append($"{{_localizer[_dto.GetMemberDescription(x=>x.{property.Name})],item => item.{property.Name}}}, \r\n");
				}
				else
				{
					var relatedObject = objectlist.FirstOrDefault(x => x.FullName.Equals(property.Type.CodeName) && x.IsEnum);
					if (relatedObject != null)
					{
						output.Append($"{{_localizer[_dto.GetMemberDescription(x=>x.{property.Name})],item => item.{property.Name}?.ToString()}}, \r\n");
					}
				}

			}
			return output.ToString();
		}

		private static string createMudTdHeaderDefinition(IntellisenseObject classObject, IEnumerable<IntellisenseObject> objectlist = null)
		{
			var output = new StringBuilder();
			var defaultfieldName = new string[] { "Name", "Description" };
			if (classObject.Properties.Where(x => x.Type.IsKnownType == true && defaultfieldName.Contains(x.Name)).Any())
			{
				output.Append($"<PropertyColumn Property=\"x => x.Name\" Title=\"@L[_currentDto.GetMemberDescription(x=>x.Name)]\"> \r\n");
				output.Append("   <CellTemplate>\r\n");
				output.Append($"      <div class=\"d-flex flex-column\">\r\n");
				if (classObject.Properties.Where(x => x.Type.IsKnownType == true && x.Name == defaultfieldName.First()).Any())
				{
					output.Append($"        <MudText Typo=\"Typo.body2\">@context.Item.Name</MudText>\r\n");
				}
				if (classObject.Properties.Where(x => x.Type.IsKnownType == true && x.Name == defaultfieldName.Last()).Any())
				{
					output.Append($"        <MudText Typo=\"Typo.body2\" Class=\"mud-text-secondary\">@context.Item.Description</MudText>\r\n");
				}
				output.Append($"     </div>\r\n");
				output.Append("    </CellTemplate>\r\n");
				output.Append($"</PropertyColumn>\r\n");
			}
			foreach (var property in classObject.Properties.Where(x => !x.Type.IsDictionary && !x.Type.IsArray && !defaultfieldName.Contains(x.Name)))
			{
				if (property.Name == PRIMARYKEY) continue;
				if (property.Type.IsKnownType)
				{
					output.Append("                ");
					output.Append($"<PropertyColumn Property=\"x => x.{property.Name}\" Title=\"@L[_currentDto.GetMemberDescription(x=>x.{property.Name})]\" />\r\n");
				}
				else
				{
					var relatedObject = objectlist.FirstOrDefault(x => x.FullName.Equals(property.Type.CodeName) && x.IsEnum);
					if (relatedObject != null)
					{
						output.Append("                ");
						output.Append($"<PropertyColumn Property=\"x => x.{property.Name}\" Title=\"@L[_currentDto.GetMemberDescription(x=>x.{property.Name})]\">\r\n");
						output.Append("                <CellTemplate>\r\n");
						output.Append($"						<MudChip T=\"string\"  Value=\"@context.Item.{property.Name}?.GetDescription()\" />\r\n");
						output.Append("                </CellTemplate>\r\n");
						output.Append($"</PropertyColumn>\r\n");
					}
				}
			}
			return output.ToString();
		}

		private static string createMudTdDefinition(IntellisenseObject classObject)
		{
			var output = new StringBuilder();
			var defaultfieldName = new string[] { "Name", "Description" };
			if (classObject.Properties.Where(x => x.Type.IsKnownType == true && defaultfieldName.Contains(x.Name)).Any())
			{
				output.Append($"<MudTd HideSmall=\"false\" DataLabel=\"@L[_currentDto.GetMemberDescription(x=>x.Name)]\"> \r\n");
				output.Append("                ");
				output.Append($"    <div class=\"d-flex flex-column\">\r\n");
				if (classObject.Properties.Where(x => x.Type.IsKnownType == true && x.Name == defaultfieldName.First()).Any())
				{
					output.Append("                ");
					output.Append($"        <MudText>@context.Name</MudText>\r\n");
				}
				if (classObject.Properties.Where(x => x.Type.IsKnownType == true && x.Name == defaultfieldName.Last()).Any())
				{
					output.Append("                ");
					output.Append($"        <MudText Typo=\"Typo.body2\" Class=\"mud-text-secondary\">@context.Description</MudText>\r\n");
				}
				output.Append("                ");
				output.Append($"    </div>\r\n");
				output.Append("                ");
				output.Append($"</MudTd>\r\n");
			}
			foreach (var property in classObject.Properties.Where(x => x.Type.IsKnownType == true && !x.Type.IsDictionary && !x.Type.IsArray && !defaultfieldName.Contains(x.Name)))
			{
				if (property.Name == PRIMARYKEY) continue;
				output.Append("                ");
				if (property.Type.CodeName.StartsWith("bool", StringComparison.OrdinalIgnoreCase))
				{
					output.Append($"        <MudTd HideSmall=\"false\" DataLabel=\"@L[_currentDto.GetMemberDescription(x=>x.{property.Name})]\" ><MudCheckBox Checked=\"@context.{property.Name}\" ReadOnly></MudCheckBox></MudTd> \r\n");
				}
				else if (property.Type.CodeName.Equals("System.DateTime", StringComparison.OrdinalIgnoreCase))
				{
					output.Append($"        <MudTd HideSmall=\"false\" DataLabel=\"@L[_currentDto.GetMemberDescription(x=>x.{property.Name}))]\" >@context.{property.Name}.Date.ToString(\"d\")</MudTd> \r\n");
				}
				else if (property.Type.CodeName.Equals("System.DateTime?", StringComparison.OrdinalIgnoreCase))
				{
					output.Append($"        <MudTd HideSmall=\"false\" DataLabel=\"@L[_currentDto.GetMemberDescription(x=>x{property.Name})]\" >@context.{property.Name}?.Date.ToString(\"d\")</MudTd> \r\n");
				}
				else
				{
					output.Append($"        <MudTd HideSmall=\"false\" DataLabel=\"@L[_currentDto.GetMemberDescription(x=>.{property.Name})]\" >@context.{property.Name}</MudTd> \r\n");
				}

			}
			return output.ToString();
		}

		private static string createMudFormFieldDefinition(IntellisenseObject classObject, IEnumerable<IntellisenseObject> objectlist = null)
		{
			var output = new StringBuilder();
			foreach (var property in classObject.Properties.Where(x => !x.Type.IsDictionary && !x.Type.IsArray))
			{
				if (property.Name == PRIMARYKEY) continue;
				switch (property.Type.CodeName.ToLower())
				{
					case "string" when property.Name.Equals("Name", StringComparison.OrdinalIgnoreCase):
						output.Append($"<MudItem xs=\"12\" md=\"6\"> \r\n");
						output.Append("                ");
						output.Append($"        <MudTextField Label=\"@L[model.GetMemberDescription(x=>x.{property.Name})]\" @bind-Value=\"model.{property.Name}\" For=\"@(() => model.{property.Name})\" Required=\"true\" RequiredError=\"@L[\"{splitCamelCase(property.Name).ToLower()} is required!\"]\"></MudTextField>\r\n");
						output.Append("                ");
						output.Append($"</MudItem> \r\n");
						break;
					case "string" when property.Name.Equals("Description", StringComparison.OrdinalIgnoreCase):
						output.Append($"<MudItem xs=\"12\" md=\"6\"> \r\n");
						output.Append("                ");
						output.Append($"        <MudTextField Label=\"@L[model.GetMemberDescription(x=>x.{property.Name})]\" Lines=\"3\" For=\"@(() => model.{property.Name})\" @bind-Value=\"model.{property.Name}\"></MudTextField>\r\n");
						output.Append("                ");
						output.Append($"</MudItem> \r\n");
						break;
					case "bool?":
					case "bool":
						output.Append($"<MudItem xs=\"12\" md=\"6\"> \r\n");
						output.Append("                ");
						output.Append($"        <MudCheckBox Label=\"@L[model.GetMemberDescription(x=>x.{property.Name})]\" @bind-Checked=\"model.{property.Name}\" For=\"@(() => model.{property.Name})\" ></MudCheckBox>\r\n");
						output.Append("                ");
						output.Append($"</MudItem> \r\n");
						break;
					case "int?":
					case "int":
						output.Append($"<MudItem xs=\"12\" md=\"6\"> \r\n");
						output.Append("                ");
						output.Append($"        <MudNumericField  Label=\"@L[model.GetMemberDescription(x=>x.{property.Name})]\" @bind-Value=\"model.{property.Name}\" For=\"@(() => model.{property.Name})\" Min=\"0\" Required=\"false\" RequiredError=\"@L[\"{splitCamelCase(property.Name).ToLower()} is required!\"]\"></MudNumericField >\r\n");
						output.Append("                ");
						output.Append($"</MudItem> \r\n");
						break;
					case "decimal?":
					case "decimal":
						output.Append($"<MudItem xs=\"12\" md=\"6\"> \r\n");
						output.Append("                ");
						output.Append($"        <MudNumericField  Label=\"@L[model.GetMemberDescription(x=>x.{property.Name})]\" @bind-Value=\"model.{property.Name}\" For=\"@(() => model.{property.Name})\" Min=\"0.00m\" Required=\"false\" RequiredError=\"@L[\"{splitCamelCase(property.Name).ToLower()} is required!\"]\"></MudNumericField >\r\n");
						output.Append("                ");
						output.Append($"</MudItem> \r\n");
						break;
					case "double?":
					case "double":
						output.Append($"<MudItem xs=\"12\" md=\"6\"> \r\n");
						output.Append("                ");
						output.Append($"        <MudNumericField  Label=\"@L[model.GetMemberDescription(x=>x.{property.Name})]\" @bind-Value=\"model.{property.Name}\" For=\"@(() => model.{property.Name})\" Min=\"0.00\" Required=\"false\" RequiredError=\"@L[\"{splitCamelCase(property.Name).ToLower()} is required!\"]\"></MudNumericField >\r\n");
						output.Append("                ");
						output.Append($"</MudItem> \r\n");
						break;
					case "system.datetime":
					case "system.datetime?":
						output.Append($"<MudItem xs=\"12\" md=\"6\"> \r\n");
						output.Append("                ");
						output.Append($"        <MudDatePicker Label=\"@L[model.GetMemberDescription(x=>x.{property.Name})]\" @bind-Date=\"model.{property.Name}\" For=\"@(() => model.{property.Name})\" Required=\"false\" RequiredError=\"@L[\"{splitCamelCase(property.Name).ToLower()} is required!\"]\"></MudDatePicker>\r\n");
						output.Append("                ");
						output.Append($"</MudItem> \r\n");
						break;
					default:
						if (property.Type.IsKnownType)
						{
							output.Append($"<MudItem xs=\"12\" md=\"6\"> \r\n");
							output.Append("                ");
							output.Append($"        <MudTextField Label=\"@L[model.GetMemberDescription(x=>x.{property.Name})]\" @bind-Value=\"model.{property.Name}\" For=\"@(() => model.{property.Name})\" Required=\"false\" RequiredError=\"@L[\"{splitCamelCase(property.Name).ToLower()} is required!\"]\"></MudTextField>\r\n");
							output.Append("                ");
							output.Append($"</MudItem> \r\n");
						}
						else
						{
							var relatedObject = objectlist.FirstOrDefault(x => x.FullName.Equals(property.Type.CodeName) && x.IsEnum);
							if (relatedObject != null)
							{
								var enumType = property.Type.CodeName.Split('.').Last();
								output.Append($"<MudItem xs=\"12\" md=\"6\"> \r\n");
								output.Append("                ");
								output.Append($"        <MudEnumSelect TEnum=\"Nullable<{enumType}>\" Label=\"@L[model.GetMemberDescription(x=>x.{property.Name})]\" @bind-Value=\"model.{property.Name}\" For=\"@(() => model.{property.Name})\" Required=\"false\" RequiredError=\"@L[\"{splitCamelCase(property.Name).ToLower()} is required!\"]\"></MudEnumSelect>\r\n");
								output.Append("                ");
								output.Append($"</MudItem> \r\n");
							}
						}
						break;

				}

			}
			return output.ToString();
		}


		private static string createFieldAssignmentDefinition(IntellisenseObject classObject)
		{
			var output = new StringBuilder();
			foreach (var property in classObject.Properties.Where(x => x.Type.IsKnownType == true && x.Name != "Id"))
			{
				output.Append($"        {property.Name} = dto.{property.Name}, \r\n");
			}
			return output.ToString();
		}

		private static string createEntityTypeBuilderConfirmation(IntellisenseObject classObject, IEnumerable<IntellisenseObject> objectlist = null)
		{
			var output = new StringBuilder();
			foreach (var property in classObject.Properties.Where(x => x.Name != "Id"))
			{
				switch (property.Type.CodeName)
				{
					case "string":
					case "string?":
						if (property.Type.IsArray)
						{
							output.Append($"    builder.Property(e => e.{property.Name}).HasStringListConversion(); \r\n");
						}
						else if (property.Type.IsDictionary)
						{
							output.Append($"    builder.Property(u => u.{property.Name}).HasJsonConversion(); \r\n");
						}
						else if (property.Name.Equals("Name"))
						{
							output.Append($"    builder.HasIndex(x => x.{property.Name}); \r\n");
							output.Append($"    builder.Property(x => x.{property.Name}).HasMaxLength(50).IsRequired(); \r\n");
						}
						else
						{
							output.Append($"    builder.Property(x => x.{property.Name}).HasMaxLength(255); \r\n");
						}
						break;
					default:
						if (!property.Type.IsKnownType)
						{
							var refObject = objectlist.FirstOrDefault(x => x.FullName.Equals(property.Type.CodeName));
							if (refObject != null)
							{
								var complexType = property.Type.CodeName.Split('.').Last();
								if (refObject.IsEnum)
								{
									output.Append($"    builder.HasIndex(x => x.{property.Name}); \r\n");
									output.Append($"    builder.Property(t => t.{property.Name}).HasConversion<string>().HasMaxLength(50); \r\n");
								}
								else if (!property.Type.IsArray)
								{
									var foreignKey = property.Name + "Id";
									if (classObject.Properties.Any(x => x.Name.Equals(foreignKey)))
									{
										output.Append($"    builder.HasOne(x => x.{property.Name}).WithMany().HasForeignKey(x => x.{foreignKey}); \r\n");
									}
								}
							}
							else
							{
								if (property.Name.Equals("Tenant") && classObject.Properties.Any(x => x.Name.Equals("TenantId")))
								{
									output.Append($"    builder.HasOne(x => x.{property.Name}).WithMany().HasForeignKey(x => x.TenantId); \r\n");
									output.Append($"    builder.Navigation(e => e.{property.Name}).AutoInclude(); \r\n");
								}
							}

						}
						break;
				}
			}

			if (classObject.BaseName.Equals("OwnerPropertyEntity"))
			{
				output.Append($"    builder.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.CreatedBy); \r\n");
				output.Append($"    builder.HasOne(x => x.LastModifier).WithMany().HasForeignKey(x => x.LastModifiedBy); \r\n");
				output.Append($"    builder.Navigation(e => e.Owner).AutoInclude(); \r\n");
				output.Append($"    builder.Navigation(e => e.LastModifier).AutoInclude(); \r\n");
			}
			return output.ToString();
		}


		private static string createComandValidatorRuleFor(IntellisenseObject classObject, IEnumerable<IntellisenseObject> objectlist = null)
		{
			var output = new StringBuilder();
			foreach (var property in classObject.Properties.Where(x => x.Name != "Id"))
			{
				switch (property.Type.CodeName)
				{
					case "string":
					case "string?":
						if (property.Name.Equals("Name"))
						{
							output.Append($"    RuleFor(v => v.{property.Name}).MaximumLength(50).NotEmpty(); \r\n");
						}
						else if (!property.Type.IsDictionary && !property.Type.IsArray)
						{
							output.Append($"    RuleFor(v => v.{property.Name}).MaximumLength(255); \r\n");
						}
						break;
					case "System.TimeSpan":
					case "System.DateTime":
					case "System.DateTimeOffset":
					case "int":
					case "decimal":
					case "float":
						output.Append($"    RuleFor(v => v.{property.Name}).NotNull(); \r\n");
						break;
					default:
						if (!property.Type.IsKnownType)
						{
							var refObject = objectlist.FirstOrDefault(x => x.FullName.Equals(property.Type.CodeName));
							if (refObject != null)
							{
								var complexType = property.Type.CodeName.Split('.').Last();
								if (refObject.IsEnum)
								{
									output.Append($"    RuleFor(v => v.{property.Name}).NotNull(); \r\n");
								}

							}
						}
						break;
				}

			}
			return output.ToString();
		}
	}
}
