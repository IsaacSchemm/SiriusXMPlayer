using Microsoft.Extensions.Logging;
using Services.Abstractions;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace SiriusXMPlayer;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    //these button classes are specific to the HTML for siriusxm. if they change their code we'll have to adjust
    private const string _siriusPreviousTrackSelector = "button[aria-label='Skip back']";
    private const string _siriusNextTrackButtonSelector = "button[aria-label='Skip forward']";
    private const string _siriusPlayPauseButtonSelector = "button[aria-label='Play'], button[aria-label='Pause']";

    private readonly MainWindowViewModel _viewModel;
    private readonly IMediaKeyEventService _mediaKeyEventService;
    private readonly ILogger<MainWindow> _logger;

    public MainWindow(MainWindowViewModel viewModel,
        Services.Abstractions.IMediaKeyEventService mediaKeyEventService,
        ILogger<MainWindow> logger)
    {
        InitializeComponent();

        this.DataContext = viewModel;
        _viewModel = viewModel;
        _viewModel.OnBound();
        _mediaKeyEventService = mediaKeyEventService;
        _logger = logger;

        browser.CoreWebView2InitializationCompleted += Browser_CoreWebView2InitializationCompleted;

        browser.NavigationCompleted += Browser_NavigationCompleted;

        this.Loaded += MainWindow_Loaded;
    }

    private void Browser_NavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
    {
        browser.ExecuteScriptAsync(@"(async () => {
            while (document.getElementsByTagName('audio').length === 0)
                await new Promise(r => setTimeout(r, 1000));

            let outputDevices = [];

            const div = document.createElement('button');
	        document.body.appendChild(div);
	        div.style.position = 'absolute';
	        div.style.right = '32px';
	        div.style.top = '16px';
	        div.style.zIndex = 4;

            const currentDeviceLabel = document.createElement('span');
            div.appendChild(currentDeviceLabel);

            const toggleButton = document.createElement('button');
            div.appendChild(toggleButton);
	        toggleButton.innerText = 'Switch audio device';
	        toggleButton.style.cursor = 'pointer';
	        toggleButton.style.marginLeft = '16px';

            const closeButton = document.createElement('button');
            div.appendChild(closeButton);
	        closeButton.innerText = '🗙';
	        closeButton.style.cursor = 'pointer';
	        closeButton.style.marginLeft = '16px';

            toggleButton.addEventListener('click', async () => {
                try {
                    const audio = document.getElementsByTagName('audio')[0];
                    if (!audio)
                        return;

                    const sinkId = audio.sinkId;

                    if (outputDevices.length === 0) {
                        let devices = await navigator.mediaDevices.enumerateDevices();
                        if (!devices.some(d => d.label)) {
                            const media = await navigator.mediaDevices.getUserMedia({ audio: true });
                            devices = await navigator.mediaDevices.enumerateDevices();
                            for (const track of media.getTracks())
                                track.stop();
                        }

                        if (!devices.some(d => d.label))
                            return;

                        outputDevices = devices
                            .filter(d => d.label)
                            .filter(d => d.kind == 'audiooutput');
                    }

                    let foundIndex = 0;

                    for (let i = 0; i < outputDevices.length; i++) {
                        const device = outputDevices[i];
                        if (device.deviceId === sinkId) {
                            foundIndex = i;
                        }
                    }

                    const nextDevice = outputDevices[(foundIndex + 1) % outputDevices.length];
                    audio.setSinkId(nextDevice.deviceId);

                    const labelText =`(${nextDevice.label || nextDevice.deviceId})`;
                    currentDeviceLabel.innerText = labelText;
                    await new Promise(r => setTimeout(r, 2000));
                    if (currentDeviceLabel.innerText == labelText)
                        currentDeviceLabel.innerText = '';
                } catch (e) {
                    console.error(e);
                    alert('Could not switch audio device.');
                }
            });

            closeButton.addEventListener('click', () => document.body.removeChild(div));
        })();");
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var t = InitializeBrowserAndSetupAsync();
    }

    private void Browser_CoreWebView2InitializationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2InitializationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            _logger.LogError(e.InitializationException, "WebView failed to intialize");

            if (e.InitializationException is Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException)
            {
                browser.Visibility = Visibility.Collapsed;
                NeedWebViewDownloadPane.Visibility = Visibility.Visible;
            }
        }
    }

    protected async Task InitializeBrowserAndSetupAsync()
    {
        await browser.EnsureCoreWebView2Async();

        //webview needs to be initialized before we start doing stuff with it, so that's why this code is here, rather than in ctor
        _mediaKeyEventService.PlayPausePressed += _mediaKeyEventService_PlayPausePressed;
        _mediaKeyEventService.NextTrackPressed += _mediaKeyEventService_NextTrackPressed;
        _mediaKeyEventService.PreviousTrackPressed += _mediaKeyEventService_PreviousTrackPressed;

        //startuplocation does not support binding, so we have to manually set it, we are banking on it not changing after initial load, so we aren't watching for property change
        this.WindowStartupLocation = _viewModel.WindowStartupLocation;

        //now that we are initialized, setup the binding
        browser.SetBinding(Microsoft.Web.WebView2.Wpf.WebView2.SourceProperty, nameof(MainWindowViewModel.SiteUrl));
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.OnUnbound();
        this.Loaded -= MainWindow_Loaded;
        browser.CoreWebView2InitializationCompleted -= Browser_CoreWebView2InitializationCompleted;

        if (_mediaKeyEventService != null)
        {
            _mediaKeyEventService.PlayPausePressed -= _mediaKeyEventService_PlayPausePressed;
            _mediaKeyEventService.NextTrackPressed -= _mediaKeyEventService_NextTrackPressed;
            _mediaKeyEventService.PreviousTrackPressed -= _mediaKeyEventService_PreviousTrackPressed;
        }
        base.OnClosed(e);
    }


    private void _mediaKeyEventService_NextTrackPressed(object? sender, EventArgs e)
    {
        _logger.LogInformation("Next Track");
        this.PressButton(_siriusNextTrackButtonSelector);
    }

    private void _mediaKeyEventService_PreviousTrackPressed(object? sender, EventArgs e)
    {
        _logger.LogInformation("Previous Track");
        this.PressButton(_siriusPreviousTrackSelector);
    }

    private void _mediaKeyEventService_PlayPausePressed(object? sender, EventArgs e)
    {
        _logger.LogInformation("PlayPause");
        this.PressButton(_siriusPlayPauseButtonSelector);
    }

    private void PressButton(string selectorName)
    {
        //sirius is not using jquery so we are using vanilla JS, which is fine
        //   we are invoking click on the first matching element... of course what if there are more than one or none... 
        //   that would mean they changed their code and we'll have to adjust this
        var t = browser.ExecuteScriptAsync($"document.querySelectorAll(\"{selectorName}\")[0].click();");
    }

    private void Exit_MenuItem_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }
}
