using Microsoft.Extensions.DependencyInjection;

namespace ObjectDetector
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            LoadTheme();
        }

        private void LoadTheme()
        {
            var savedTheme = Preferences.Get("AppTheme", "System");
            
            UserAppTheme = savedTheme switch
            {
                "Dark" => AppTheme.Dark,
                "Light" => AppTheme.Light,
                _ => AppTheme.Unspecified
            };
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }
    }
}