using CefSharp;
using CefSharp.OffScreen;
using SVN.CefSharp.Handler;
using SVN.Core.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SVN.CefSharp
{
    public class CWB : IDisposable
    {
        private ChromiumWebBrowser Browser { get; set; }
        private string Source { get; set; }
        public string Proxy { get; private set; }
        private static List<string> Proxies { get; } = new List<string>();
        private List<string> Errors { get; } = new List<string>();

        public string GetErrors
        {
            get
            {
                lock (this.Errors)
                {
                    var result = this.Errors;
                    this.Errors.Clear();
                    return result.Join("\n");
                }
            }
        }

        public CWB()
        {
            var browserSettings = new BrowserSettings
            {
            };
            var requestSettings = new RequestContextSettings
            {
                CachePath = @"C:\cef-cache",
            };
            var requestContext = new RequestContext(requestSettings);

            Cef.UIThreadTaskFactory.StartNew(delegate
            {
                lock (CWB.Proxies)
                {
                    if (CWB.Proxies.Any())
                    {
                        this.Proxy = CWB.Proxies.ElementAt(0);
                        CWB.Proxies.RemoveAt(0);
                        CWB.Proxies.Add(this.Proxy);

                        var dict = new Dictionary<string, object>
                        {
                            ["mode"] = "fixed_servers",
                            ["server"] = this.Proxy,
                        };
                        var success = requestContext.SetPreference("proxy", dict, out string error);

                        if (!success || !string.IsNullOrWhiteSpace(error))
                        {
                            lock (this.Errors)
                            {
                                this.Errors.Add(error);
                            }
                        }
                    }
                }
            });

            this.Browser = new ChromiumWebBrowser(string.Empty, browserSettings, requestContext)
            {
                DialogHandler = new DialogHandler(),
                JsDialogHandler = new JsDialogHandler(),
                LifeSpanHandler = new LifeSpanHandler(),
            };
            this.Browser.FrameLoadEnd += this.FrameLoadEnd;

            if (!this.Browser.IsBrowserInitialized)
            {
                this.Browser.RegisterJsObject("print", new { });
            }
            while (!this.Browser.IsBrowserInitialized)
            {
                Thread.Sleep(TimeSpan.FromSeconds(1));
            }
        }

        public void Dispose()
        {
            this.Browser?.Stop();
            this.Browser?.Dispose();
            this.Browser?.RequestContext?.Dispose();
            this.Browser = null;
        }

        public static void Initialize(params string[] proxies)
        {
            var settings = new CefSettings
            {
                CachePath = @"C:\cef-cache",
                IgnoreCertificateErrors = true,
                LogFile = @"C:\cef-cache\log.txt",
                LogSeverity = LogSeverity.Error,
            };
            lock (CWB.Proxies)
            {
                CWB.Proxies.Clear();
                CWB.Proxies.AddRange(proxies);
            }
            //settings.CefCommandLineArgs.Add("proxy-server", "x.x.x.x:x");
            settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/72.0.3626.109 Safari/537.36";

            CefSharpSettings.LegacyJavascriptBindingEnabled = true;
            CefSharpSettings.ShutdownOnExit = false;

            Cef.Initialize(settings, true, browserProcessHandler: null);
        }

        public static void Shutdown()
        {
            Cef.Shutdown();
        }

        public string GetSourceCode(string url)
        {
            lock (this.Browser)
            {
                this.Source = null;
                this.Browser.Load(url);

                //Thread.Sleep(TimeSpan.FromSeconds(10));

                //if (this.Browser.CanExecuteJavascriptInMainFrame)
                //{
                //    return this.Browser.EvaluateScriptAsync(@"document.getElementsByTagName('html')[0].innerHTML").Result.Result.ToString();
                //}
                //else
                //{
                //    return "cant execute js in main-frame";
                //}

                var startTime = DateTime.Now;
                var timeout = TimeSpan.FromMinutes(1);

                while (this.Source is null && DateTime.Now < startTime.Add(timeout))
                {
                    Thread.Sleep(TimeSpan.FromSeconds(1));
                }

                return this.Source ?? string.Empty;
            }
        }

        public void ExecuteScript(string script)
        {
            this.Browser.GetMainFrame().ExecuteJavaScriptAsync(script);
        }

        private void FrameLoadEnd(object sender, FrameLoadEndEventArgs e)
        {
            if (e.Frame.IsMain)
            {
                this.Browser.GetSourceAsync().ContinueWith(x =>
                {
                    this.Source = x.Result;
                });
            }
        }
    }
}