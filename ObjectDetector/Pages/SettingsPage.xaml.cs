namespace ObjectDetector.Pages
{
    public partial class SettingsPage : ContentPage
    {
        public SettingsPage()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            // TODO: Load settings from preferences
        }

        private void OnConfidenceChanged(object sender, ValueChangedEventArgs e)
        {
            ConfidenceLabel.Text = $"{e.NewValue:F2}";
            // TODO: Save to preferences
        }

        private void OnIouChanged(object sender, ValueChangedEventArgs e)
        {
            IouLabel.Text = $"{e.NewValue:F2}";
            // TODO: Save to preferences
        }
    }
}