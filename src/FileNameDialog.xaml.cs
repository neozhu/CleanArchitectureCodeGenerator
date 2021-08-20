using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace CleanArchitecture.CodeGenerator
{
	public partial class FileNameDialog : Window
	{
		private const string DEFAULT_TEXT = "Select a entity name";
		private static readonly List<string> _tips = new List<string> {
			"Tip: An effective testing strategy that follows the testing pyramid",
			"Tip: CQRS stands for Command/Query Responsibility Segregation, and it's a wonderful thing",
			"Tip: All business logic is in a use case",
			"Tip: Good monolith with clear use cases that you can split in microservices later on, once you’ve learned more about them ",
			"Tip: CI/CD processes and solutions help to generate more value for the end-users of software",
			"Tip: the architecture is decoupled from the underlying data store"
		};

		public FileNameDialog(string folder,string[] entities)
		{
			InitializeComponent();
			lblFolder.Content = string.Format("{0}/", folder);
			foreach(var item in entities)
			{
				selectName.Items.Add(item);
			}
			selectName.Text = DEFAULT_TEXT;
			selectName.SelectionChanged += (s,e) => {
				btnCreate.IsEnabled = true;
			};
				Loaded += (s, e) =>
			{
				Icon = BitmapFrame.Create(new Uri("pack://application:,,,/CleanArchitectureCodeGenerator;component/Resources/icon.png", UriKind.RelativeOrAbsolute));
				Title = Vsix.Name;
				SetRandomTip();

				//txtName.Focus();
				//txtName.CaretIndex = 0;
				//txtName.Text = DEFAULT_TEXT;
				//txtName.Select(0, txtName.Text.Length);

				//txtName.PreviewKeyDown += (a, b) =>
				//{
				//	if (b.Key == Key.Escape)
				//	{
				//		if (string.IsNullOrWhiteSpace(txtName.Text) || txtName.Text == DEFAULT_TEXT)
				//		{
				//			Close();
				//		}
				//		else
				//		{
				//			txtName.Text = string.Empty;
				//		}
				//	}
				//	else if (txtName.Text == DEFAULT_TEXT)
				//	{
				//		txtName.Text = string.Empty;
				//		btnCreate.IsEnabled = true;
				//	}
				//};

			};
		}

		public string Input => selectName.SelectedItem?.ToString();

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
