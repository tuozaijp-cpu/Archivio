using System;
using System.IO;
using Archivio.ViewModels;
using Archivio.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Archivio
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? window;

        public static Window? MainWindow { get; private set; }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            window ??= new Window();
            MainWindow = window;
            window.Title = "Archivio";
            window.AppWindow.SetIcon("Assets/Archivio.ico");
            window.ExtendsContentIntoTitleBar = false;

            // Load saved settings
            var settings = SettingsManager.LoadSettings();
            LanguageManager.Initialize();

            if (window.AppWindow.Presenter is OverlappedPresenter presenter)
            {
                // Restore saved size or fallback to default
                window.AppWindow.Resize(new Windows.Graphics.SizeInt32(settings.WindowWidth, settings.WindowHeight));
            }

            if (window.Content is not Frame rootFrame)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                window.Content = rootFrame;
            }

            _ = rootFrame.Navigate(typeof(MainPage), e.Arguments);
            window.Activate();

            // Save window size and grid layouts on close
            window.Closed += (sender, args) =>
            {
                try
                {
                    var currentSettings = SettingsManager.LoadSettings();
                    
                    // Save window size
                    currentSettings.WindowWidth = window.AppWindow.Size.Width;
                    currentSettings.WindowHeight = window.AppWindow.Size.Height;

                    // Save resizable layout sizes from MainPage
                    if (window.Content is Frame frame && frame.Content is MainPage page)
                    {
                        currentSettings.IncludeSubfolders = page.ViewModel.IncludeSubfolders;
                        currentSettings.IsThumbnailView = page.ViewModel.IsThumbnailView;
                        currentSettings.ThumbnailTileSizeIndex = page.ViewModel.ThumbnailTileSizeIndex;
                        currentSettings.LastFolderPath = page.ViewModel.FolderPath;
                        page.SaveLayoutSettings(currentSettings);
                    }

                    SettingsManager.SaveSettings(currentSettings);
                }
                catch (Exception ex)
                {
                    AppLogger.Error("アプリケーション終了時の設定保存に失敗しました", ex);
                }
            };
        }

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation failure</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }
    }
}
