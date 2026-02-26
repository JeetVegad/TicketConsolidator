using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace TicketConsolidator.UI.Views
{
    public partial class TemplateEditorView : UserControl
    {
        private bool _browserReady;

        public TemplateEditorView(TemplateEditorViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Initialize WebView2
            try
            {
                await PreviewBrowser.EnsureCoreWebView2Async();
                _browserReady = true;

                // Wire up ViewModel preview events
                if (DataContext is TemplateEditorViewModel vm)
                {
                    vm.PreviewRequested += OnPreviewRequested;
                    vm.PropertyChanged += OnViewModelPropertyChanged;

                    // Render initial preview
                    if (!string.IsNullOrEmpty(vm.PreviewHtml))
                    {
                        PreviewBrowser.NavigateToString(vm.PreviewHtml);
                    }
                }
            }
            catch
            {
                // WebView2 runtime may not be installed
                _browserReady = false;
            }

            // Track caret position for placeholder insertion
            HtmlEditor.TextArea.Caret.PositionChanged += (s, args) =>
            {
                if (DataContext is TemplateEditorViewModel viewModel)
                {
                    viewModel.CaretOffset = HtmlEditor.TextArea.Caret.Offset;
                }
            };

            // Ctrl+S shortcut
            KeyDown += (s, args) =>
            {
                if (args.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    if (DataContext is TemplateEditorViewModel viewModel)
                    {
                        viewModel.SaveCommand.Execute(null);
                    }
                    args.Handled = true;
                }
            };

            // Set HTML syntax highlighting (built-in)
            try
            {
                HtmlEditor.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance
                    .GetDefinition("HTML");
            }
            catch { /* Highlighting is optional — editor still works without it */ }
        }

        private void OnPreviewRequested(string html)
        {
            if (_browserReady && !string.IsNullOrEmpty(html))
            {
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        PreviewBrowser.NavigateToString(html);
                    }
                    catch { /* WebView2 may be disposed during navigation */ }
                });
            }
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TemplateEditorViewModel.PreviewHtml) && _browserReady)
            {
                var vm = (TemplateEditorViewModel)sender;
                if (!string.IsNullOrEmpty(vm.PreviewHtml))
                {
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            PreviewBrowser.NavigateToString(vm.PreviewHtml);
                        }
                        catch { }
                    });
                }
            }
        }
    }
}
