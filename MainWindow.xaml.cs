using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Images2PDF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            this.DataContext = new MainVM();
        }

        private MainVM GetDataContext()
        {
            return this.DataContext as MainVM;
        }

        private void SelectFolderClicked(object sender, RoutedEventArgs e)
        {
            var d = new SaveFileDialog();
            d.Title = "Select Image Directory";
            var result = d.ShowDialog();
            if(result == true)
            {
                var info = new FileInfo(d.FileName);
                var vm = GetDataContext();
                if(vm != null)
                {
                    vm.Logs = "";
                    vm.SourceDirectoryPath = info.DirectoryName;
                    vm.SourceFolderName = info.Directory.Name;
                }
            }
        }
    }
}
