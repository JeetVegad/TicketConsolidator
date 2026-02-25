using System;
using System.Collections.Generic;
using System.Net;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace TicketConsolidator.UI.Views
{
    public partial class JiraLoginWindow : Window
    {
        private readonly string _loginUrl;
        private bool _browserInitialized;

        /// <summary>Cookies extracted from the browser session after the user clicks Done.</summary>
        public List<Cookie> ExtractedCookies { get; private set; }

        /// <summary>The base URL used for cookie extraction scope.</summary>
        public string CookieBaseUrl { get; set; }

        public JiraLoginWindow(string loginUrl, string cookieBaseUrl)
        {
            InitializeComponent();
            _loginUrl = loginUrl;
            CookieBaseUrl = cookieBaseUrl;

            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                NavigationProgress.Visibility = Visibility.Visible;
                StatusText.Text = "Initializing browser...";

                await JiraBrowser.EnsureCoreWebView2Async();
                _browserInitialized = true;

                // Hook navigation events
                JiraBrowser.CoreWebView2.NavigationStarting += OnNavigationStarting;
                JiraBrowser.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
                JiraBrowser.CoreWebView2.SourceChanged += OnSourceChanged;

                // Navigate to Jira login
                JiraBrowser.CoreWebView2.Navigate(_loginUrl);

                // Advance step indicator
                AdvanceToStep(2);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Browser initialization failed: {ex.Message}";
                NavigationProgress.Visibility = Visibility.Collapsed;
            }
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            NavigationProgress.Visibility = Visibility.Visible;
            UpdateUrlDisplay(e.Uri);
        }

        private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            NavigationProgress.Visibility = Visibility.Collapsed;

            if (!e.IsSuccess)
            {
                StatusText.Text = "Page load failed — check your network connection.";
            }
            else
            {
                StatusText.Text = "Complete the Jira login, then click 'I'm Logged In'";
            }
        }

        private void OnSourceChanged(object sender, CoreWebView2SourceChangedEventArgs e)
        {
            if (JiraBrowser.CoreWebView2 != null)
            {
                UpdateUrlDisplay(JiraBrowser.CoreWebView2.Source);
            }
        }

        private void UpdateUrlDisplay(string url)
        {
            if (string.IsNullOrEmpty(url)) return;

            try
            {
                var uri = new Uri(url);
                UrlDisplay.Text = uri.Host + uri.PathAndQuery;

                // Update lock icon color based on HTTPS
                LockIcon.Foreground = uri.Scheme == "https"
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50))
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x98, 0x00));
            }
            catch
            {
                UrlDisplay.Text = url;
            }
        }

        private async void Done_Click(object sender, RoutedEventArgs e)
        {
            if (!_browserInitialized || JiraBrowser.CoreWebView2 == null)
            {
                StatusText.Text = "Browser not ready yet — please wait.";
                return;
            }

            try
            {
                StatusText.Text = "Extracting session cookies...";
                AdvanceToStep(3);

                var cookieManager = JiraBrowser.CoreWebView2.CookieManager;
                var webCookies = await cookieManager.GetCookiesAsync(CookieBaseUrl);

                ExtractedCookies = new List<Cookie>();
                foreach (var wc in webCookies)
                {
                    ExtractedCookies.Add(new Cookie(wc.Name, wc.Value, wc.Path, wc.Domain));
                }

                DialogResult = true;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Cookie extraction failed: {ex.Message}";
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void AdvanceToStep(int step)
        {
            // Step 1 is always complete once loaded
            Step1.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50)); // Green = done

            if (step >= 2)
            {
                Step2.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x00, 0x52, 0xCC)); // Blue = active
                Step2Text.Foreground = System.Windows.Media.Brushes.White;
            }

            if (step >= 3)
            {
                Step2.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50)); // Green = done
                Step3.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x00, 0x52, 0xCC)); // Blue = active
                Step3Text.Foreground = System.Windows.Media.Brushes.White;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // Clean up WebView2 events
            if (_browserInitialized && JiraBrowser.CoreWebView2 != null)
            {
                JiraBrowser.CoreWebView2.NavigationStarting -= OnNavigationStarting;
                JiraBrowser.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                JiraBrowser.CoreWebView2.SourceChanged -= OnSourceChanged;
            }

            JiraBrowser.Dispose();
            base.OnClosed(e);
        }
    }
}
