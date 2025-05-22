using CleanArchitecture.CodeGenerator.Helpers;
using CleanArchitecture.CodeGenerator.Models;
using EnvDTE;
using EnvDTE80;
using Microsoft;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Threading;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace CleanArchitecture.CodeGenerator
{
	[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
	[InstalledProductRegistration("#110", "#112", Vsix.Version, IconResourceID = 400)]
	[ProvideMenuResource("Menus.ctmenu", 1)]
	[Guid(PackageGuids.guidCodeGeneratorPkgString)]
	public sealed class CodeGeneratorPackage : AsyncPackage
	{
		public const string DOMAINPROJECT = "Domain";
		public const string UIPROJECT = "Server.UI";
		public const string INFRASTRUCTUREPROJECT = "Infrastructure";
		public const string APPLICATIONPROJECT = "Application";

		private const string _solutionItemsProjectName = "Solution Items";
		private static readonly Regex _reservedFileNamePattern = new Regex($@"(?i)^(PRN|AUX|NUL|CON|COM\d|LPT\d)(\.|$)");
		private static readonly HashSet<char> _invalidFileNameChars = new HashSet<char>(Path.GetInvalidFileNameChars());

		public static DTE2 _dte;

		protected async override Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
		{
			// Switch to the main thread - the call to GetServiceAsync() done below requires it
			await JoinableTaskFactory.SwitchToMainThreadAsync();

			// Initialize DTE object for interaction with Visual Studio environment
			_dte = await GetServiceAsync(typeof(DTE)) as DTE2;
			Assumes.Present(_dte); // Ensure DTE is successfully initialized

			Logger.Initialize(this, Vsix.Name); // Initialize logger for the extension

			// Get the menu command service and add the command handler for the menu item defined in the .vsct file
			if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService mcs)
			{
				CommandID menuCommandID = new CommandID(PackageGuids.guidCodeGeneratorCmdSet, PackageIds.cmdidMyCommand);
				OleMenuCommand menuItem = new OleMenuCommand(ExecuteAsync, menuCommandID);
				mcs.AddCommand(menuItem);
			}
		}

		private void ExecuteAsync(object sender, EventArgs e)
		{
			NewItemTarget target = NewItemTarget.Create(_dte);
			NewItemTarget domain= NewItemTarget.Create(_dte, DOMAINPROJECT);
			NewItemTarget infrastructure = NewItemTarget.Create(_dte, INFRASTRUCTUREPROJECT);
			NewItemTarget ui = NewItemTarget.Create(_dte, UIPROJECT);
			var includes = new string[] { "IEntity", "BaseEntity", "BaseAuditableEntity", "BaseAuditableSoftDeleteEntity", "AuditTrail", "OwnerPropertyEntity","KeyValue" };
		 
			var objectlist = ProjectHelpers.GetEntities(domain.Project)
				.Where(x => x.IsEnum || (includes.Contains(x.BaseName) && !includes.Contains(x.Name)));
			var entities = objectlist.Where(x=>x.IsEnum==false).Select(x=>x.Name).ToArray();
			if (target == null && target.Project.Name == APPLICATIONPROJECT)
			{
				MessageBox.Show(
						"Unable to determine the location for creating the new file. Please select a folder within the Application Project in the Explorer and try again.",
						Vsix.Name,
						MessageBoxButton.OK,
						MessageBoxImage.Error);
				return;
			}

			string input = PromptForFileName(target.Directory,entities).TrimStart('/', '\\').Replace("/", "\\");

			if (string.IsNullOrEmpty(input))
			{
				return;
			}

			string[] parsedInputs = GetParsedInput(input);

			foreach (string inputname in parsedInputs)
			{
				try
				{
					var name = Path.GetFileNameWithoutExtension(inputname);
					var nameofPlural = ProjectHelpers.Pluralize(name);
					var objectClass = objectlist.Where(x => x.Name == name).First();
					GenerateDomainItemsAsync(objectClass, name, domain, objectlist).Forget();
					GenerateInfrastructureItemsAsync(objectClass, name, nameofPlural, infrastructure, objectlist).Forget();
					GenerateApplicationItemsAsync(objectClass, name, nameofPlural, target, objectlist).Forget();
					GenerateUiItemsAsync(objectClass, name, nameofPlural, ui, objectlist).Forget();
				}
				catch (Exception ex) when (!ErrorHandler.IsCriticalException(ex))
				{
					Logger.Log(ex);
					MessageBox.Show(
							$"Error creating file '{inputname}':{Environment.NewLine}{ex.Message}",
							Vsix.Name,
							MessageBoxButton.OK,
							MessageBoxImage.Error);
				}
			}
		}

		private async Task GenerateDomainItemsAsync(IntellisenseObject objectClass, string name, NewItemTarget domainTarget, IEnumerable<IntellisenseObject> objectlist)
		{
			var events = new List<string>() {
				$"Events/{name}CreatedEvent.cs",
				$"Events/{name}DeletedEvent.cs",
				$"Events/{name}UpdatedEvent.cs",
			};
			foreach (var item in events)
			{
				AddItemAsync(objectClass, item, name, domainTarget, objectlist).Forget();
			}
		}

		private async Task GenerateInfrastructureItemsAsync(IntellisenseObject objectClass, string name, string nameofPlural, NewItemTarget infrastructureTarget, IEnumerable<IntellisenseObject> objectlist)
		{
			var configurations = new List<string>() {
				$"Persistence/Configurations/{name}Configuration.cs",
				$"PermissionSet/{nameofPlural}.cs"
			};
			foreach (var item in configurations)
			{
				AddItemAsync(objectClass, item, name, infrastructureTarget, objectlist).Forget();
			}
		}

		private async Task GenerateApplicationItemsAsync(IntellisenseObject objectClass, string name, string nameofPlural, NewItemTarget applicationTarget, IEnumerable<IntellisenseObject> objectlist)
		{
			var list = new List<string>()
			{
				$"{nameofPlural}/Commands/AddEdit/AddEdit{name}Command.cs",
				$"{nameofPlural}/Commands/AddEdit/AddEdit{name}CommandValidator.cs",
				$"{nameofPlural}/Commands/Create/Create{name}Command.cs",
				$"{nameofPlural}/Commands/Create/Create{name}CommandValidator.cs",
				$"{nameofPlural}/Commands/Delete/Delete{name}Command.cs",
				$"{nameofPlural}/Commands/Delete/Delete{name}CommandValidator.cs",
				$"{nameofPlural}/Commands/Update/Update{name}Command.cs",
				$"{nameofPlural}/Commands/Update/Update{name}CommandValidator.cs",
				$"{nameofPlural}/Commands/Import/Import{nameofPlural}Command.cs",
				$"{nameofPlural}/Commands/Import/Import{nameofPlural}CommandValidator.cs",
				$"{nameofPlural}/Caching/{name}CacheKey.cs",
				$"{nameofPlural}/DTOs/{name}Dto.cs",
				/*$"{nameofPlural}/Mappers/{name}Mapper.cs",*/
				$"{nameofPlural}/EventHandlers/{name}CreatedEventHandler.cs",
				$"{nameofPlural}/EventHandlers/{name}UpdatedEventHandler.cs",
				$"{nameofPlural}/EventHandlers/{name}DeletedEventHandler.cs",
				$"{nameofPlural}/Specifications/{name}AdvancedFilter.cs",
				$"{nameofPlural}/Specifications/{name}AdvancedSpecification.cs",
				$"{nameofPlural}/Specifications/{name}ByIdSpecification.cs",
				$"{nameofPlural}/Queries/Export/Export{nameofPlural}Query.cs",
				$"{nameofPlural}/Queries/GetAll/GetAll{nameofPlural}Query.cs",
				$"{nameofPlural}/Queries/GetById/Get{name}ByIdQuery.cs",
				$"{nameofPlural}/Queries/Pagination/{nameofPlural}PaginationQuery.cs",
			};
			foreach (var item in list)
			{
				AddItemAsync(objectClass, item, name, applicationTarget, objectlist).Forget();
			}
		}

		private async Task GenerateUiItemsAsync(IntellisenseObject objectClass, string name, string nameofPlural, NewItemTarget uiTarget, IEnumerable<IntellisenseObject> objectlist)
		{
			var pages = new List<string>()
			{
				$"Pages/{nameofPlural}/Create{name}.razor",
				$"Pages/{nameofPlural}/Edit{name}.razor",
				$"Pages/{nameofPlural}/View{name}.razor",
				$"Pages/{nameofPlural}/{nameofPlural}.razor",
				$"Pages/{nameofPlural}/Components/{name}FormDialog.razor",
				$"Pages/{nameofPlural}/Components/{nameofPlural}AdvancedSearchComponent.razor"
			};
			foreach (var item in pages)
			{
				AddItemAsync(objectClass, item, name, uiTarget, objectlist).Forget();
			}
		}

		/// <summary>
		/// Adds a new item (folder or file) to the specified target location in the solution.
		/// This method determines whether to create a folder or a file based on the 'name' parameter.
		/// </summary>
		/// <param name="classObject">The IntellisenseObject representing the class or entity for which the item is generated. Used by templating.</param>
		/// <param name="name">The relative path and name of the item to be created (e.g., "NewFolder/" or "NewFile.cs").
		/// If it ends with a backslash, a folder is created; otherwise, a file is created.</param>
		/// <param name="itemname">The name of the primary entity or class being generated (e.g., "Product"). This is often used by the templating engine to customize content.</param>
		/// <param name="target">The target location (project or folder) where the new item will be added.</param>
		/// <param name="objectlist">An optional list of other IntellisenseObjects, potentially used by the templating engine for context or related data.</param>
		private async Task AddItemAsync(IntellisenseObject classObject, string name, string itemname, NewItemTarget target, IEnumerable<IntellisenseObject> objectlist = null)
		{
			try
			{
				// The naming rules that apply to files created on disk also apply to virtual solution folders,
				// so regardless of what type of item we are creating, we need to validate the path.
				ValidatePath(name);

				// Check if the 'name' indicates a folder (ends with a backslash).
				if (name.EndsWith("\\", StringComparison.Ordinal))
				{
					// If the target is a solution or a solution folder, use GetOrAddSolutionFolder.
					if (target.IsSolutionOrSolutionFolder)
					{
						GetOrAddSolutionFolder(name, target);
					}
					else // Otherwise, it's a project folder.
					{
						AddProjectFolder(name, target);
					}
				}
				else // 'name' does not end with a backslash, so it's a file.
				{
					await AddFileAsync(classObject, name, itemname, target, objectlist);
				}
			}
			catch (Exception ex)
			{
				// Log the exception, potentially with more context if available (like 'name' or 'itemname')
				Logger.Log($"Error in AddItemAsync for item '{name}': {ex.ToString()}");
				// Consider if re-throwing or specific UI feedback is needed, 
				// but for a .Forget() task, logging is primary.
			}
		}

		/// <summary>
		/// Validates each segment of the given path for reserved names and invalid characters.
		/// </summary>
		/// <param name="path">The path to validate.</param>
		/// <exception cref="InvalidOperationException">Thrown if a path segment is a reserved name or contains invalid characters.</exception>
		private void ValidatePath(string path)
		{
			do
			{
				string name = Path.GetFileName(path); // Get the last segment of the current path

				// Check against a regex for reserved system file names (e.g., PRN, AUX, NUL, CON, COM1-9, LPT1-9).
				if (_reservedFileNamePattern.IsMatch(name))
				{
					throw new InvalidOperationException($"The name '{name}' is a system reserved name.");
				}

				// Check if the segment contains any characters that are invalid for file names.
				if (name.Any(c => _invalidFileNameChars.Contains(c)))
				{
					throw new InvalidOperationException($"The name '{name}' contains invalid characters.");
				}

				path = Path.GetDirectoryName(path); // Move to the parent directory segment
			} while (!string.IsNullOrEmpty(path)); // Continue until all segments are validated
		}

		/// <summary>
		/// Adds a file to the project, writes content to it based on templates, and opens it in the editor.
		/// </summary>
		/// <param name="classObject">Intellisense object representing the class, used for template selection and content generation.</param>
		/// <param name="name">The relative path and name of the file to create (e.g., "DTOs/MyDto.cs").</param>
		/// <param name="itemname">The core name of the item being generated (e.g., "MyDto" from "MyDto.cs"). This is passed to <see cref="WriteFileAsync"/> and is crucial for template processing to generate class-specific content.</param>
		/// <param name="target">The target project or folder where the file will be added.</param>
		/// <param name="objectlist">A list of other Intellisense objects, passed to <see cref="WriteFileAsync"/> for more complex template scenarios that might require information about related entities or types.</param>
		private async Task AddFileAsync(IntellisenseObject classObject, string name,string itemname, NewItemTarget target,IEnumerable<IntellisenseObject> objectlist=null)
		{
			await JoinableTaskFactory.SwitchToMainThreadAsync(); // Ensure UI operations are on the main thread
			FileInfo file;

			// Determine the absolute file path.
			// If the target is a solution folder that doesn't map to a physical directory (a virtual folder),
			// the file is created in the solution's root directory. This is a common fallback.
			if (target.IsSolutionFolder && !Directory.Exists(target.Directory))
			{
				file = new FileInfo(Path.Combine(Path.GetDirectoryName(_dte.Solution.FullName), Path.GetFileName(name)));
			}
			else // Otherwise, the file is created within the target's physical directory.
			{
				file = new FileInfo(Path.Combine(target.Directory, name));
			}

			// Ensure the directory for the file exists.
			// Directory.CreateDirectory is used as it's robust and creates all necessary parent directories.
			Directory.CreateDirectory(file.DirectoryName);

			if (!file.Exists) // Only proceed if the file doesn't already exist.
			{
				Project project;

				// Determine the correct project context for adding the file.
				if (target.IsSolutionOrSolutionFolder)
				{
					// If it's a solution or solution folder, ensure the folder structure exists in the Solution Explorer.
					project = GetOrAddSolutionFolder(Path.GetDirectoryName(name), target);
				}
				else
				{
					// Otherwise, use the target project directly.
					project = target.Project;
				}

				// Write the file content using templates.
				// 'itemname' and 'objectlist' are passed here for the templating engine (TemplateMap.GetTemplateFilePathAsync).
				int position = await WriteFileAsync(project, classObject, file.FullName, itemname, target.Directory, objectlist);
				
				// Add the newly created file to the project in Solution Explorer.
				if (target.ProjectItem != null && target.ProjectItem.IsKind(Constants.vsProjectItemKindVirtualFolder))
				{
					// If the target is a virtual folder, add the file to its ProjectItems collection.
					target.ProjectItem.ProjectItems.AddFromFile(file.FullName);
				}
				else
				{
					// Otherwise, use a helper to add the file to the project.
					project.AddFileToProject(file);
				}

				// Open the newly created file in the Visual Studio editor.
				VsShellUtilities.OpenDocument(this, file.FullName);

				// If the template specified a cursor position (e.g., for filling in a method body), move the caret.
				if (position > 0)
				{
					Microsoft.VisualStudio.Text.Editor.IWpfTextView view = ProjectHelpers.GetCurentTextView();

					if (view != null)
					{
						view.Caret.MoveTo(new SnapshotPoint(view.TextBuffer.CurrentSnapshot, position));
					}
				}

				// Sync Solution Explorer with the active document and activate the document.
				ExecuteCommandIfAvailable("SolutionExplorer.SyncWithActiveDocument");
				_dte.ActiveDocument.Activate(); // Ensures the newly opened file gets focus
			}
			else
			{
				// Optionally, inform the user that the file already exists.
				//MessageBox.Show($"The file '{file}' already exists.", Vsix.Name, MessageBoxButton.OK, MessageBoxImage.Information);
				Console.WriteLine($"The file '{file}' already exists."); // Currently logs to output.
			}
		}

		/// <summary>
		/// Writes content to a file, determining the content by retrieving a template.
		/// </summary>
		/// <param name="project">The project context, used by TemplateMap.</param>
		/// <param name="classObject">The IntellisenseObject for which the file is being generated, used by TemplateMap.</param>
		/// <param name="file">The full path of the file to write.</param>
		/// <param name="itemname">The name of the item (e.g., class name), used by TemplateMap to fetch the correct template and customize its content.</param>
		/// <param name="selectFolder">The selected folder path, used by TemplateMap.</param>
		/// <param name="objectlist">A list of other IntellisenseObjects, used by TemplateMap for context in template selection/generation.</param>
		/// <returns>The desired cursor position within the file after writing, or 0 if not specified by the template.</returns>
		private static async Task<int> WriteFileAsync(Project project, IntellisenseObject classObject,  string file,string itemname,string selectFolder,IEnumerable<IntellisenseObject> objectlist=null)
		{
			string template = await TemplateMap.GetTemplateFilePathAsync(project, classObject,file, itemname, selectFolder, objectlist);

			if (!string.IsNullOrEmpty(template))
			{
				int index = template.IndexOf('$');

				if (index > -1)
				{
					//template = template.Remove(index, 1);
				}

				await WriteToDiskAsync(file, template);
				return index;
			}

			await WriteToDiskAsync(file, string.Empty);

			return 0;
		}

		private static async Task WriteToDiskAsync(string file, string content)
		{
			using (StreamWriter writer = new StreamWriter(file, false, GetFileEncoding(file)))
			{
				await writer.WriteAsync(content);
			}
		}

		private static Encoding GetFileEncoding(string file)
		{
			string[] noBom = { ".cmd", ".bat", ".json" };
			string ext = Path.GetExtension(file).ToLowerInvariant();

			if (noBom.Contains(ext))
			{
				return new UTF8Encoding(false);
			}

			return new UTF8Encoding(true);
		}

		/// <summary>
		/// Gets an existing solution folder or creates it if it doesn't exist.
		/// Handles adding to the solution root (via a "Solution Items" folder) or a specific solution folder hierarchy.
		/// </summary>
		/// <param name="name">The path of the solution folder (e.g., "Folder1/SubFolder").
		/// If empty and target is the solution, it defaults to a "Solution Items" folder.</param>
		/// <param name="target">The target location, which can be the solution itself or another solution folder.</param>
		/// <returns>The Project object representing the solution folder.</returns>
		private Project GetOrAddSolutionFolder(string name, NewItemTarget target)
		{
			// If targeting the solution root and no specific folder name is provided,
			// items are added to a default "Solution Items" folder. This is a VS convention.
			if (target.IsSolution && string.IsNullOrEmpty(name))
			{
				return _dte.Solution.FindSolutionFolder(_solutionItemsProjectName)
						?? ((Solution2)_dte.Solution).AddSolutionFolder(_solutionItemsProjectName);
			}

			// Although solution folders are primarily virtual constructs in the Solution Explorer,
			// if the target directory (where the solution file resides or a parent physical folder) exists,
			// this code also creates corresponding physical directories on disk.
			// This ensures that if files are later added to this solution folder via the file system,
			// they appear correctly within the solution folder's mapped physical location.
			if (Directory.Exists(target.Directory))
			{
				Directory.CreateDirectory(Path.Combine(target.Directory, name));
			}

			Project parent = target.Project; // Start with the target's project (can be null if target is solution root)

			// Iterate through each segment of the folder path (e.g., "Folder1", then "SubFolder").
			foreach (string segment in SplitPath(name))
			{
				// If parent is null, it means we are at the solution root level.
				if (parent == null)
				{
					parent = _dte.Solution.FindSolutionFolder(segment) ?? ((Solution2)_dte.Solution).AddSolutionFolder(segment);
				}
				else // Otherwise, we are adding a subfolder to an existing solution folder.
				{
					parent = parent.FindSolutionFolder(segment) ?? ((SolutionFolder)parent.Object).AddSolutionFolder(segment);
				}
			}
			return parent; // Returns the innermost Project object representing the final folder.
		}

		/// <summary>
		/// Adds a physical folder to a project, creating it segment by segment if necessary.
		/// </summary>
		/// <param name="name">The relative path of the folder to create (e.g., "NewFolder/SubFolder").</param>
		/// <param name="target">The target project or project item (like a parent folder) where the new folder will be added.</param>
		private void AddProjectFolder(string name, NewItemTarget target)
		{
			ThreadHelper.ThrowIfNotOnUIThread(); // Ensure UI operations are on the main thread.

			// Ensure the physical directory exists on disk.
			Directory.CreateDirectory(Path.Combine(target.Directory, name));

			// Get the initial collection of items to add to (either project's root items or a subfolder's items).
			ProjectItems items = target.ProjectItem?.ProjectItems ?? target.Project.ProjectItems;
			string parentDirectory = target.Directory;

			// Iterate through each segment of the folder path (e.g., "NewFolder", then "SubFolder").
			// This is done because ProjectItems.AddFromDirectory usually only adds the final segment
			// if the full path is given. Adding segment by segment ensures the entire hierarchy is
			// reflected in the Solution Explorer.
			foreach (string segment in SplitPath(name))
			{
				parentDirectory = Path.Combine(parentDirectory, segment); // Construct path for current segment

				// Check if a folder with this name already exists in the current ProjectItems collection.
				ProjectItem folder = items
						.OfType<ProjectItem>()
						.Where(item => segment.Equals(item.Name, StringComparison.OrdinalIgnoreCase))
						.Where(item => item.IsKind(Constants.vsProjectItemKindPhysicalFolder, Constants.vsProjectItemKindVirtualFolder))
						.FirstOrDefault();

				// If the folder doesn't exist in the project, add it from the directory.
				if (folder == null)
				{
					folder = items.AddFromDirectory(parentDirectory);
				}
				// Update 'items' to the ProjectItems collection of the newly found/created folder for the next iteration.
				items = folder.ProjectItems;
			}
		}

		/// <summary>
		/// Splits a file path into its constituent directory or file name segments.
		/// </summary>
		/// <param name="path">The path string to split.</param>
		/// <returns>An array of path segments.</returns>
		private static string[] SplitPath(string path)
		{
			return path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
		}

		private static string[] GetParsedInput(string input)
		{
			// var tests = new string[] { "file1.txt", "file1.txt, file2.txt", ".ignore", ".ignore.(old,new)", "license", "folder/",
			//    "folder\\", "folder\\file.txt", "folder/.thing", "page.aspx.cs", "widget-1.(html,js)", "pages\\home.(aspx, aspx.cs)",
			//    "home.(html,js), about.(html,js,css)", "backup.2016.(old, new)", "file.(txt,txt,,)", "file_@#d+|%.3-2...3^&.txt" };
			Regex pattern = new Regex(@"[,]?([^(,]*)([\.\/\\]?)[(]?((?<=[^(])[^,]*|[^)]+)[)]?");
			List<string> results = new List<string>();
			Match match = pattern.Match(input);

			while (match.Success)
			{
				// Always 4 matches w. Group[3] being the extension, extension list, folder terminator ("/" or "\"), or empty string
				string path = match.Groups[1].Value.Trim() + match.Groups[2].Value;
				string[] extensions = match.Groups[3].Value.Split(',');

				foreach (string ext in extensions)
				{
					string value = path + ext.Trim();

					// ensure "file.(txt,,txt)" or "file.txt,,file.txt,File.TXT" returns as just ["file.txt"]
					if (value != "" && !value.EndsWith(".", StringComparison.Ordinal) && !results.Contains(value, StringComparer.OrdinalIgnoreCase))
					{
						results.Add(value);
					}
				}
				match = match.NextMatch();
			}
			return results.ToArray();
		}

		private string PromptForFileName(string folder,string[] entities)
		{
			DirectoryInfo dir = new DirectoryInfo(folder);
			FileNameDialog dialog = new FileNameDialog(dir.Name, entities);

			//IntPtr hwnd = new IntPtr(_dte.MainWindow.HWnd); // DTE interaction to get main window handle
			//System.Windows.Window window = (System.Windows.Window)HwndSource.FromHwnd(hwnd).RootVisual;
			dialog.Owner = Application.Current.MainWindow;

			bool? result = dialog.ShowDialog();
			return (result.HasValue && result.Value) ? dialog.Input : string.Empty;
		}

		/// <summary>
		/// Executes a DTE command if it exists and is available.
		/// </summary>
		/// <param name="commandName">The name of the command to execute (e.g., "SolutionExplorer.SyncWithActiveDocument").</param>
		private void ExecuteCommandIfAvailable(string commandName)
		{
			ThreadHelper.ThrowIfNotOnUIThread(); // Commands often need to run on the UI thread.
			Command command;

			try
			{
				// Attempt to retrieve the command from the DTE Commands collection.
				command = _dte.Commands.Item(commandName);
			}
			catch (ArgumentException)
			{
				// The command does not exist in the current DTE environment.
				Logger.Log($"Command '{commandName}' not found.");
				return;
			}

			// Check if the command is currently available (e.g., contextually enabled).
			if (command.IsAvailable)
			{
				_dte.ExecuteCommand(commandName); // Execute the command.
			}
			else
			{
				Logger.Log($"Command '{commandName}' is not available.");
			}
		}
	}
}