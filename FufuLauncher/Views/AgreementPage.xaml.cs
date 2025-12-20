using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views
{
    public sealed partial class AgreementPage : Page
    {
        public AgreementViewModel ViewModel
        {
            get;
        }

        public AgreementPage()
        {
            ViewModel = App.GetService<AgreementViewModel>();
            InitializeComponent();
        }
    }
}