using CleanArchitecture.CodeGenerator.Models;
using EnvDTE;

using EnvDTE80;

using Microsoft;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;

using System;
using System.Collections.Generic;
using System.Data.Entity.Design.PluralizationServices;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace CleanArchitecture.CodeGenerator.Helpers
{
	public static class ProjectHelpers
	{
		private static readonly DTE2 _dte = CodeGeneratorPackage._dte;

		/// <summary>
		/// Gets the root namespace of the project.
		/// It first tries to get the "RootNamespace" property, and falls back to the project name if the property is not available.
		/// </summary>
		/// <param name="project">The project.</param>
		/// <returns>The root namespace of the project, or null if the project is null.</returns>
		public static string GetRootNamespace(this Project project)
		{
			if (project == null)
			{
				return null;
			}

			string ns = project.Name ?? string.Empty;

			try
			{
				Property prop = project.Properties.Item("RootNamespace");

				if (prop != null && prop.Value != null && !string.IsNullOrEmpty(prop.Value.ToString()))
				{
					ns = prop.Value.ToString();
				}
			}
			catch (ArgumentException ex) 
			{ 
				Logger.Log($"Project {project.Name} might not have a RootNamespace property. Details: {ex.Message}"); 
			}

			return CleanNameSpace(ns, stripPeriods: false);
		}

		/// <summary>
		/// Cleans a namespace string by removing spaces, hyphens, and optionally periods.
		/// Replaces backslashes with periods.
		/// </summary>
		/// <param name="ns">The namespace string to clean.</param>
		/// <param name="stripPeriods">If true, periods will be removed from the namespace. Defaults to true.</param>
		/// <returns>The cleaned namespace string.</returns>
		public static string CleanNameSpace(string ns, bool stripPeriods = true)
		{
			if (stripPeriods)
			{
				ns = ns.Replace(".", "");
			}

			ns = ns.Replace(" ", "")
					 .Replace("-", "")
					 .Replace("\\", ".");

			return ns;
		}

		/// <summary>
		/// Gets the root folder of the project.
		/// Handles different project types and falls back to various properties to determine the path.
		/// </summary>
		/// <param name="project">The project.</param>
		/// <returns>The root folder path of the project, or null if it cannot be determined.</returns>
		public static string GetRootFolder(this Project project)
		{
			if (project == null)
			{
				return null;
			}

			// Handle Solution Folders explicitly, as they don't have a "FullPath" property in the same way projects do.
			// Their logical "root" is the directory of the solution file itself.
			if (project.IsKind("{66A26720-8FB5-11D2-AA7E-00C04F688DDE}")) //ProjectKinds.vsProjectKindSolutionFolder
			{
				return Path.GetDirectoryName(_dte.Solution.FullName);
			}

			if (string.IsNullOrEmpty(project.FullName))
			{
				return null;
			}

			string fullPath;

			try
			{
				// Attempt to get the "FullPath" property, which is common for many project types.
				fullPath = project.Properties.Item("FullPath").Value as string;
			}
			catch (ArgumentException ex)
			{
				Logger.Log($"Failed to get FullPath for {project.Name}. Attempting next property. Details: {ex.Message}");
				try
				{
					// Fallback for project types like MFC that use "ProjectDirectory".
					fullPath = project.Properties.Item("ProjectDirectory").Value as string;
				}
				catch (ArgumentException ex2)
				{
					Logger.Log($"Failed to get ProjectDirectory for {project.Name}. Attempting next property. Details: {ex2.Message}");
					try
					{
						// Fallback for project types like Installer projects that use "ProjectPath".
						fullPath = project.Properties.Item("ProjectPath").Value as string;
					}
					catch (ArgumentException ex3)
					{
						Logger.Log($"Failed to get ProjectPath for {project.Name}. All property attempts failed. Details: {ex3.Message}");
						fullPath = null; // Ensure fullPath is null if all attempts fail.
					}
				}
			}

			if (string.IsNullOrEmpty(fullPath))
			{
				// If no property provided a path, but the project's FullName points to an existing file,
				// use its directory. This can be a last resort for some project types.
				return File.Exists(project.FullName) ? Path.GetDirectoryName(project.FullName) : null;
			}

			// Validate the obtained path.
			if (Directory.Exists(fullPath))
			{
				return fullPath; // Path is a directory, return as is.
			}

			if (File.Exists(fullPath))
			{
				return Path.GetDirectoryName(fullPath); // Path is a file, return its directory.
			}

			return null; // Path is invalid or doesn't exist.
		}

		/// <summary>
		/// Adds a file to the specified project.
		/// Skips adding if the project is an ASP.NET 5 or SSDT project, as they typically include files automatically.
		/// </summary>
		/// <param name="project">The project to add the file to.</param>
		/// <param name="file">The FileInfo object representing the file to add.</param>
		/// <param name="itemType">Optional. The item type to set for the file in the project (e.g., "Compile", "Content").</param>
		/// <returns>The created ProjectItem, or null if the file was not added.</returns>
		public static ProjectItem AddFileToProject(this Project project, FileInfo file, string itemType = null)
		{
			if (project.IsKind(ProjectTypes.ASPNET_5, ProjectTypes.SSDT))
			{
				return _dte.Solution.FindProjectItem(file.FullName);
			}

			string root = project.GetRootFolder();

			if (string.IsNullOrEmpty(root) || !file.FullName.StartsWith(root, StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}

			ProjectItem item = project.ProjectItems.AddFromFile(file.FullName);
			item.SetItemType(itemType);
			return item;
		}

		/// <summary>
		/// Sets the item type (e.g., "Compile", "Content", "None") for a project item.
		/// Does nothing if the item, containing project, or itemType is null/empty,
		/// or if the project is a Website or Universal App project (which manage item types differently).
		/// </summary>
		/// <param name="item">The project item whose type is to be set.</param>
		/// <param name="itemType">The item type string.</param>
		public static void SetItemType(this ProjectItem item, string itemType)
		{
			try
			{
				if (item == null || item.ContainingProject == null)
				{
					return;
				}

				// Certain project types (like Website projects or Universal Apps)
				// might not support or need explicit ItemType settings this way.
				if (string.IsNullOrEmpty(itemType)
					|| item.ContainingProject.IsKind(ProjectTypes.WEBSITE_PROJECT)
					|| item.ContainingProject.IsKind(ProjectTypes.UNIVERSAL_APP))
				{
					return;
				}

				item.Properties.Item("ItemType").Value = itemType;
			}
			catch (ArgumentException ex) 
			{ 
				Logger.Log($"Failed to set ItemType for '{item?.Name}': Property not found or invalid. Details: {ex.Message}"); 
			} 
			catch (COMException ex) 
			{ 
				Logger.Log($"COM error setting ItemType for '{item?.Name}'. Details: {ex.Message}"); 
			}
		}

		/// <summary>
		/// Gets the full path of a ProjectItem.
		/// </summary>
		/// <param name="item">The project item.</param>
		/// <returns>The full path of the item, or null if the property doesn't exist.</returns>
		public static string GetFileName(this ProjectItem item)
		{
			try
			{
				return item?.Properties?.Item("FullPath").Value?.ToString();
			}
			catch (ArgumentException)
			{
				// The property does not exist.
				return null;
			}
		}

		/// <summary>
		/// Finds a solution folder by name within the solution.
		/// </summary>
		/// <param name="solution">The solution.</param>
		/// <param name="name">The name of the solution folder to find.</param>
		/// <returns>The Project object representing the solution folder, or null if not found.</returns>
		public static Project FindSolutionFolder(this Solution solution, string name)
		{
			return solution.Projects.OfType<Project>()
					.Where(p => p.IsKind(EnvDTE.Constants.vsProjectKindSolutionItems)) // Solution Items are solution folders at the root
					.Where(p => p.Name == name)
					.FirstOrDefault();
		}

		/// <summary>
		/// Finds a project by name within the solution, searching recursively through solution folders.
		/// </summary>
		/// <param name="solution">The solution.</param>
		/// <param name="name">The name of the project to find.</param>
		/// <returns>The Project object if found.</returns>
		/// <exception cref="Exception">Thrown if the project with the specified name is not found.</exception>
		public static Project FindProject(this Solution solution, string name)
		{
			var project = GetProject(solution.Projects.OfType<Project>(), name);

			if (project == null)
			{
				throw new Exception($"Project {name} not found in solution");
			}
			return project;
		}

		/// <summary>
		/// Recursively searches for a project by name within a collection of projects and their sub-projects (if they are solution folders).
		/// </summary>
		/// <param name="projects">An enumerable of projects to search within.</param>
		/// <param name="name">The name of the project to find.</param>
		/// <returns>The found Project object, or null if not found in the provided collection or its descendants.</returns>
		private static Project GetProject(IEnumerable<Project> projects, string name)
		{
			foreach (Project project in projects)
			{
				var projectName = project.Name;
				if (projectName == name)
				{
					return project; // Project found
				}
				// If the current project is a solution folder, recursively search its child projects.
				// Note: ProjectKinds.vsProjectKindSolutionFolder is a string constant "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}"
				else if (project.Kind == EnvDTE.Constants.vsProjectKindSolutionItems || project.Kind == ProjectKinds.SOLUTION_FOLDER_GUID)
				{
					// Get sub-projects contained within this solution folder
					var subProjects = project
							.ProjectItems
							.OfType<ProjectItem>()
							.Where(item => item.SubProject != null) // Ensure there is a sub-project
							.Select(item => item.SubProject);

					var projectInFolder = GetProject(subProjects, name); // Recursive call

					if (projectInFolder != null)
					{
						return projectInFolder; // Project found in a subfolder
					}
				}
			}
			return null; // Project not found in this branch of the search
		}

		/// <summary>
		/// Finds a solution folder by name within the ProjectItems of a given project (typically a solution folder itself).
		/// </summary>
		/// <param name="project">The parent project (often a solution folder) to search within.</param>
		/// <param name="name">The name of the solution folder to find.</param>
		/// <returns>The Project object representing the found solution folder (as a SubProject), or null if not found.</returns>
		public static Project FindSolutionFolder(this Project project, string name)
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			return project.ProjectItems.OfType<ProjectItem>()
					.Where(p => p.IsKind(EnvDTE.Constants.vsProjectItemKindSolutionItems)) // Check if the ProjectItem itself is a solution folder
					.Where(p => p.Name == name)
					.Select(p => p.SubProject) // The actual "Project" object for a solution folder is its SubProject
					.FirstOrDefault();
		}

		/// <summary>
		/// Checks if a project is of one of the specified kinds (by GUID).
		/// </summary>
		/// <param name="project">The project.</param>
		/// <param name="kindGuids">An array of project kind GUIDs to check against.</param>
		/// <returns>True if the project's kind matches any of the provided GUIDs, false otherwise.</returns>
		public static bool IsKind(this Project project, params string[] kindGuids)
		{
			foreach (string guid in kindGuids)
			{
				if (project.Kind.Equals(guid, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Checks if a project item is of one of the specified kinds (by GUID).
		/// </summary>
		/// <param name="projectItem">The project item.</param>
		/// <param name="kindGuids">An array of project item kind GUIDs to check against.</param>
		/// <returns>True if the project item's kind matches any of the provided GUIDs, false otherwise.</returns>
		public static bool IsKind(this ProjectItem projectItem, params string[] kindGuids)
		{
			foreach (string guid in kindGuids)
			{
				if (projectItem.Kind.Equals(guid, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}

			return false;
		}

		private static IEnumerable<Project> GetChildProjects(Project parent)
		{
			try
			{
				// Check for unloaded projects:
				// An unloaded project might not be of kind "SolutionFolder" and its Collection might be null.
				// The constant "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}" is ProjectKinds.vsProjectKindSolutionFolder.
				if (!parent.IsKind(ProjectKinds.SOLUTION_FOLDER_GUID) && parent.Collection == null)  // Unloaded project check
				{
					return Enumerable.Empty<Project>(); // Return empty if unloaded
				}

				// If the parent has a FullName, it's considered a project file (not a solution folder to recurse into here)
				// or it's a loaded project we want to return.
				if (!string.IsNullOrEmpty(parent.FullName))
				{
					return new[] { parent }; // Return the project itself
				}
			}
			catch (COMException ex) // Catch COM exceptions that might occur when accessing properties of unloaded/unavailable projects
			{
				Logger.Log($"COMException while checking child projects for {parent.Name}: {ex.Message}");
				return Enumerable.Empty<Project>(); // Return empty on error
			}

			// If it's a solution folder (or similar container with ProjectItems that have SubProjects),
			// recursively get child projects.
			return parent.ProjectItems
					.Cast<ProjectItem>() // Iterate through project items
					.Where(p => p.SubProject != null) // Select items that are themselves projects
					.SelectMany(p => GetChildProjects(p.SubProject)); // Recursively call GetChildProjects
		}

		/// <summary>
		/// Gets the currently active project in the Visual Studio environment.
		/// It first checks ActiveSolutionProjects, then falls back to the project containing the active document.
		/// </summary>
		/// <returns>The active Project object, or null if no project is active or an error occurs.</returns>
		public static Project GetActiveProject()
		{
			try
			{
				// DTE.ActiveSolutionProjects returns an array of selected projects in Solution Explorer.
				// If any project is selected, the first one is considered active.
				if (_dte.ActiveSolutionProjects is Array activeSolutionProjects && activeSolutionProjects.Length > 0)
				{
					return activeSolutionProjects.GetValue(0) as Project;
				}

				// If no project is directly selected in Solution Explorer,
				// try to get the project containing the currently active document.
				Document doc = _dte.ActiveDocument;

				if (doc != null && !string.IsNullOrEmpty(doc.FullName))
				{
					ProjectItem item = _dte.Solution?.FindProjectItem(doc.FullName);

					if (item != null)
					{
						return item.ContainingProject;
					}
				}
			}
			catch (COMException ex) 
			{ 
				Logger.Log($"COM error getting active project. Details: {ex.Message}"); 
			} 
			catch (Exception ex) 
			{ 
				Logger.Log($"Generic error getting active project. Details: {ex.Message}"); 
			}

			return null;
		}

		/// <summary>
		/// Gets the current IWpfTextView active in the editor.
		/// </summary>
		/// <returns>The active IWpfTextView, or null if not found.</returns>
		public static IWpfTextView GetCurentTextView()
		{
			IComponentModel componentModel = GetComponentModel();
			if (componentModel == null)
			{
				return null;
			}

			IVsEditorAdaptersFactoryService editorAdapter = componentModel.GetService<IVsEditorAdaptersFactoryService>();

			return editorAdapter.GetWpfTextView(GetCurrentNativeTextView());
		}

		/// <summary>
		/// Gets the current native IVsTextView active in the editor.
		/// </summary>
		/// <returns>The active IVsTextView.</returns>
		public static IVsTextView GetCurrentNativeTextView()
		{
			IVsTextManager textManager = (IVsTextManager)ServiceProvider.GlobalProvider.GetService(typeof(SVsTextManager));
			Assumes.Present(textManager); // Ensures textManager is not null.

			ErrorHandler.ThrowOnFailure(textManager.GetActiveView(1, null, out IVsTextView activeView)); // 1 means true for fMustHaveFocus
			return activeView;
		}

		/// <summary>
		/// Gets the IComponentModel service from the global service provider.
		/// </summary>
		/// <returns>The IComponentModel service instance.</returns>
		public static IComponentModel GetComponentModel()
		{
			return (IComponentModel)CodeGeneratorPackage.GetGlobalService(typeof(SComponentModel));
		}

		/// <summary>
		/// Gets the currently selected object in the Visual Studio UI (typically in Solution Explorer).
		/// </summary>
		/// <returns>The selected object (e.g., Project, ProjectItem), or null if nothing is selected or an error occurs.</returns>
		public static object GetSelectedItem()
		{
			object selectedObject = null;

			IVsMonitorSelection monitorSelection = (IVsMonitorSelection)Package.GetGlobalService(typeof(SVsShellMonitorSelection));

			try
			{
				monitorSelection.GetCurrentSelection(out IntPtr hierarchyPointer,
												 out uint itemId,
												 out IVsMultiItemSelect multiItemSelect,
												 out IntPtr selectionContainerPointer);


				if (Marshal.GetTypedObjectForIUnknown(
													 hierarchyPointer,
													 typeof(IVsHierarchy)) is IVsHierarchy selectedHierarchy)
				{
					ErrorHandler.ThrowOnFailure(selectedHierarchy.GetProperty(itemId, (int)__VSHPROPID.VSHPROPID_ExtObject, out selectedObject));
				}

				Marshal.Release(hierarchyPointer); // Release COM object
				Marshal.Release(selectionContainerPointer); // Release COM object
			}
			catch (COMException ex) 
			{ 
				Logger.Log($"COM error getting selected item. Details: {ex.Message}"); 
				System.Diagnostics.Debug.Write(ex); 
			} 
			catch (InvalidCastException ex) 
			{ 
				Logger.Log($"Failed to cast selected item hierarchy. Details: {ex.Message}"); 
				System.Diagnostics.Debug.Write(ex); 
			} 
			catch (Exception ex) 
			{ 
				Logger.Log($"Generic error getting selected item. Details: {ex.Message}"); 
				System.Diagnostics.Debug.Write(ex); 
			}

			return selectedObject;
		}

		/// <summary>
		/// Retrieves a list of IntellisenseObject instances by parsing relevant code files within a project.
		/// This method traverses project items recursively.
		/// </summary>
		/// <param name="project">The project to scan for entities.</param>
		/// <returns>An enumerable of IntellisenseObject found in the project.</returns>
		public static IEnumerable<IntellisenseObject> GetEntities(this Project project)
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			var list=new List<IntellisenseObject>();
			// Iterate through top-level project items.
			foreach(ProjectItem projectitem in project.ProjectItems)
			{
				 // Recursively process each item and its children.
				 GetProjectItem(projectitem, list);
			}
			return list;
		}

		/// <summary>
		/// Recursive helper method to process a project item and its children,
		/// parsing code files to extract IntellisenseObjects.
		/// </summary>
		/// <param name="item">The project item to process.</param>
		/// <param name="list">The list to add found IntellisenseObjects to.</param>
		private static void GetProjectItem(ProjectItem item ,List<IntellisenseObject> list)
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			// If the item has sub-items (e.g., it's a folder), recurse.
			if (item.ProjectItems != null)
			{
				foreach(ProjectItem projectItem in item.ProjectItems)
				{
					 GetProjectItem(projectItem,list); // Recursive call
				}
			}
			// If the item has a FileCodeModel, it's likely a code file that can be parsed.
			if (item.FileCodeModel !=null)
			{
				// IntellisenseParser processes the file's code model to find relevant objects.
				var objects= IntellisenseParser.ProcessFile(item);
				if (objects != null)
				{
					list.AddRange(objects); // Add found objects to the list.
				}
			}
		}

		/// <summary>
		/// Pluralizes a given string using English pluralization rules.
		/// </summary>
		/// <param name="name">The string to pluralize.</param>
		/// <returns>The pluralized string.</returns>
		public static string Pluralize(string name)
		{
			return PluralizationService.CreateService(new CultureInfo("en-US")).Pluralize(name);
		}
	}

	/// <summary>
	/// Contains GUID constants for various Visual Studio project types.
	/// </summary>
	public static class ProjectTypes
	{
		public const string ASPNET_5 = "{8BB2217D-0F2D-49D1-97BC-3654ED321F3B}";
		public const string DOTNET_Core = "{9A19103F-16F7-4668-BE54-9A1E7A4F7556}";
		public const string WEBSITE_PROJECT = "{E24C65DC-7377-472B-9ABA-BC803B73C61A}";
		public const string UNIVERSAL_APP = "{262852C6-CD72-467D-83FE-5EEB1973A190}";
		public const string NODE_JS = "{9092AA53-FB77-4645-B42D-1CCCA6BD08BD}";
		public const string SSDT = "{00d1a9c2-b5f0-4af3-8072-f6c62b433612}";
		public const string SOLUTION_FOLDER_GUID = "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}"; // EnvDTE.Constants.vsProjectKindSolutionItems alternative
	}
}
