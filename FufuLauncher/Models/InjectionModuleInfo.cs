/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using CommunityToolkit.Mvvm.ComponentModel;

namespace FufuLauncher.Models
{
    public partial class InjectionModuleInfo : ObservableObject
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        [ObservableProperty]
        private bool _isSelected;
    }
}
