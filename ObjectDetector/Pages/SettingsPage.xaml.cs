using System.Diagnostics;

namespace ObjectDetector.Pages
{
    public partial class SettingsPage : ContentPage
    {
        private const string KeepScreenAliveKey = "KeepScreenAlive";
        private const string ThemeKey = "AppTheme";
        private const string ConfidenceThresholdKey = "ConfidenceThreshold";
        private const string IouThresholdKey = "IouThreshold";
        public const string UiScaleFactorKey = "UiScaleFactor";

        public SettingsPage()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            KeepScreenAliveSwitch.IsToggled = Preferences.Get(KeepScreenAliveKey, false);
            
            var savedTheme = Preferences.Get(ThemeKey, "System");
            ThemePicker.SelectedIndex = savedTheme switch
            {
                "Light" => 1,
                "Dark" => 2,
                _ => 0 // System Default
            };

            var confidenceThreshold = Preferences.Get(ConfidenceThresholdKey, 0.25f);
            ConfidenceSlider.Value = confidenceThreshold;
            ConfidenceLabel.Text = $"{confidenceThreshold:F2}";

            var iouThreshold = Preferences.Get(IouThresholdKey, 0.45f);
            IouSlider.Value = iouThreshold;
            IouLabel.Text = $"{iouThreshold:F2}";

            var uiScaleFactor = Preferences.Get(UiScaleFactorKey, 0.7f);
            UiScaleSlider.Value = uiScaleFactor;
            UiScaleLabel.Text = $"{uiScaleFactor:F2}";
        }

        private void OnConfidenceChanged(object sender, ValueChangedEventArgs e)
        {
            ConfidenceLabel.Text = $"{e.NewValue:F2}";
            Preferences.Set(ConfidenceThresholdKey, (float)e.NewValue);
        }

        private void OnIouChanged(object sender, ValueChangedEventArgs e)
        {
            IouLabel.Text = $"{e.NewValue:F2}";
            Preferences.Set(IouThresholdKey, (float)e.NewValue);
        }

        private void OnUiScaleChanged(object sender, ValueChangedEventArgs e)
        {
            UiScaleLabel.Text = $"{e.NewValue:F2}";
            Preferences.Set(UiScaleFactorKey, (float)e.NewValue);
        }

        private void OnKeepScreenAliveToggled(object sender, ToggledEventArgs e)
        {
            Preferences.Set(KeepScreenAliveKey, e.Value);
            DeviceDisplay.Current.KeepScreenOn = e.Value;
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (ThemePicker.SelectedIndex == -1)
                return;

            var selectedTheme = ThemePicker.SelectedIndex switch
            {
                1 => "Light",
                2 => "Dark",
                _ => "System"
            };

            Preferences.Set(ThemeKey, selectedTheme);
            
            if (Application.Current != null)
            {
                Application.Current.UserAppTheme = selectedTheme switch
                {
                    "Light" => AppTheme.Light,
                    "Dark" => AppTheme.Dark,
                    _ => AppTheme.Unspecified
                };
            }
        }

        private async void OnOpenFolderClicked(object sender, EventArgs e)
        {
            try
            {
                string saveDir = GetSaveDirectory();

#if WINDOWS
                Process.Start("explorer.exe", saveDir);
#elif ANDROID
                OpenAndroidFolder(saveDir);
#elif IOS || MACCATALYST
                await Launcher.OpenAsync(new OpenFileRequest
                {
                    File = new ReadOnlyFile(saveDir)
                });
#else
                await DisplayAlertAsync("Folder Location", $"Files saved to:\n{saveDir}", "OK");
#endif
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Open folder error: {ex}");
                string saveDir = GetSaveDirectory();
                await DisplayAlertAsync("Folder Location", $"Files saved to:\n{saveDir}", "OK");
            }
        }

        private string GetSaveDirectory()
        {
#if ANDROID
            return Path.Combine(Android.OS.Environment.GetExternalStoragePublicDirectory(
                Android.OS.Environment.DirectoryPictures)!.AbsolutePath, "ObjectDetector");
#elif WINDOWS
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ObjectDetector");
#elif IOS || MACCATALYST
            return Path.Combine(FileSystem.AppDataDirectory, "Captures");
#else
            return FileSystem.AppDataDirectory;
#endif
        }

#if ANDROID
        private void OpenAndroidFolder(string saveDir)
        {
            try
            {
                var intent = new Android.Content.Intent(Android.Content.Intent.ActionView);
                var uri = Android.Net.Uri.Parse(saveDir);
                intent.SetData(uri);
                Android.App.Application.Context.StartActivity(intent);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open folder: {ex.Message}");
            }
        }
#endif
    }
}