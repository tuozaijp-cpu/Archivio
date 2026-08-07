using Archivio.ViewModels;

namespace Archivio.Models
{
    public class FilterValueItem : ViewModelBase
    {
        private bool _isChecked;

        public string Value { get; set; } = string.Empty;

        public bool IsChecked
        {
            get => _isChecked;
            set => SetProperty(ref _isChecked, value);
        }
    }
}
