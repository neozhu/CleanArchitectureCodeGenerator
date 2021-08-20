using CleanArchitecture.CodeGenerator.Helpers;
using CleanArchitecture.CodeGenerator.Models;
using EnvDTE;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Threading;

namespace CleanArchitecture.CodeGenerator.Services
{
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("csharp")]
    [ContentType("basic")]
    [TextViewRole(PredefinedTextViewRoles.PrimaryDocument)]
    public class GenerationService : IWpfTextViewCreationListener
    {
        private ProjectItem _item;

        [Import]
        public ITextDocumentFactoryService _documentService { get; set; }

        public void TextViewCreated(IWpfTextView textView)
        {
            if (!_documentService.TryGetTextDocument(textView.TextBuffer, out var doc))
                return;

            _item = VSHelpers.GetProjectItem(doc.FilePath);

            if (_item?.ContainingProject == null ||
                !_item.ContainingProject.IsKind(ProjectTypes.DOTNET_Core, ProjectTypes.ASPNET_5, ProjectTypes.WEBSITE_PROJECT))
                return;

            doc.FileActionOccurred += FileActionOccurred;
        }

        private void FileActionOccurred(object sender, TextDocumentFileActionEventArgs e)
        {
            if (e.FileActionType != FileActionTypes.ContentSavedToDisk)
                return;
            _item = VSHelpers.GetProjectItem(e.FilePath);
            string fileName = GenerationService.GenerateFileName(e.FilePath);

            if (File.Exists(fileName))
            {
                CreateDtsFile(_item);
            }
        }

        public static string ConvertToTypeScript(ProjectItem sourceItem)
        {
            try
            {
                VSHelpers.WriteOnOutputWindow(string.Format("{0} - Started", sourceItem.Name));
                var list = IntellisenseParser.ProcessFile(sourceItem);
                VSHelpers.WriteOnOutputWindow(string.Format("{0} - Completed", sourceItem.Name));
                return IntellisenseWriter.WriteTypeScript(list);
            }
            catch (Exception ex)
            {
                VSHelpers.WriteOnOutputWindow(string.Format("{0} - Failure", sourceItem.Name));
                return null;
            }
        }

        public static string GenerateFileName(string sourceFile)
        {
           
                return Path.ChangeExtension(sourceFile, ".d.ts");
         
        }

        public static void CreateDtsFile(ProjectItem sourceItem)
        {
            string sourceFile = sourceItem.FileNames[1];
            string dtsFile = GenerationService.GenerateFileName(sourceFile);
            string dts = ConvertToTypeScript(sourceItem);

            VSHelpers.CheckFileOutOfSourceControl(dtsFile);
            File.WriteAllText(dtsFile, dts);

            if (sourceItem.ContainingProject.IsKind(ProjectTypes.DOTNET_Core, ProjectTypes.ASPNET_5))
            {
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    var dtsItem = VSHelpers.GetProjectItem(dtsFile);

                    if (dtsItem != null)
                        dtsItem.Properties.Item("DependentUpon").Value = sourceItem.Name;

                }), DispatcherPriority.ApplicationIdle, null);
            }
            else if (sourceItem.ContainingProject.IsKind(ProjectTypes.WEBSITE_PROJECT))
            {
                sourceItem.ContainingProject.ProjectItems.AddFromFile(dtsFile);
            }
        }
    }
}
