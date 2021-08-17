using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace CleanArchitecture.CodeGenerator
{
	public partial class FileNameDialog : Window
	{
		private const string DEFAULT_TEXT = "Enter a file name";
		private static readonly List<string> _tips = new List<string> {
			"Tip: 'folder/file.ext' also creates a new folder for the file",
			"Tip: You can create files starting with a dot, like '.gitignore'",
			"Tip: You can create files without file extensions, like 'LICENSE'",
			"Tip: Create folder by ending the name with a forward slash",
			"Tip: Use glob style syntax to add related files, like 'widget.(html,js)'",
			"Tip: Separate names with commas to add multiple files and folders"
		};

		public FileNameDialog(string folder)
		{
			InitializeComponent();

			lblFolder.Content = string.Format("{0}/", folder);

			Loaded += (s, e) =>
			{
				Icon = BitmapFrame.Create(new Uri("pack://application:,,,/AddAnyFile;component/Resources/icon.png", UriKind.RelativeOrAbsolute));
				Title = Vsix.Name;
				SetRandomTip();

				txtName.Focus();
				txtName.CaretIndex = 0;
				txtName.Text = DEFAULT_TEXT;
				txtName.Select(0, txtName.Text.Length);

				txtName.PreviewKeyDown += (a, b) =>
				{
					if (b.Key == Key.Escape)
					{
						if (string.IsNullOrWhiteSpace(txtName.Text) || txtName.Text == DEFAULT_TEXT)
						{
							Close();
						}
						else
						{
							txtName.Text = string.Empty;
						}
					}
					else if (txtName.Text == DEFAULT_TEXT)
					{
						txtName.Text = string.Empty;
						btnCreate.IsEnabled = true;
					}
				};

			};
		}

		public string Input => txtName.Text.Trim();

		private void SetRandomTip()
		{
			Random rnd = new Random(DateTime.Now.GetHashCode());
			int index = rnd.Next(_tips.Count);
			lblTips.Content = _tips[index];
		}

		private void Button_Click(object sender, RoutedEventArgs e)
		{
			DialogResult = true;
			Close();
		}
	}
}
