using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml; 

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

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            if (!ViewModel.IsIconCheckMode)
            {
                EntranceStoryboard.Begin();
            }
        }

        private void OnAgreementScrollChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (ViewModel.HasReadAgreement) return;
            var sv = (ScrollViewer)sender;
            if (sv.VerticalOffset >= sv.ScrollableHeight - 10)
            {
                ViewModel.HasReadAgreement = true;
            }
        }
    }
}