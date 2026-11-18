using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace VioletStudio.Desktop;

public class MainForm : Form
{
    private readonly WebView2 webView = new() { Dock = DockStyle.Fill };
    private Process? server;

    public MainForm()
    {
        Text = "Violet Studio";
        Width = 1280;
        Height = 860;
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        Controls.Add(webView);
        Load += async (_, __) => await InitAsync();
        FormClosing += (_, __) => { try { if (server != null && !server.HasExited) server.Kill(true); } catch { } };
    }

    private async Task InitAsync()
    {
        try
        {
            await webView.EnsureCoreWebView2Async();
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            webView.CoreWebView2.DownloadStarting += CoreWebView2_DownloadStarting;
            StartServer();
            var url = "http://127.0.0.1:5123";
            await WaitForServer(url);
            webView.Source = new Uri(url);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Uygulama başlatılamadı:\n" + ex.Message, "Violet Studio", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private void CoreWebView2_DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        try
        {
            var deferral = e.GetDeferral();
            // Show save dialog on UI thread
            BeginInvoke(new Action(() =>
            {
                try
                {
                    var suggested = e.ResultFilePath;
                    var suggestedName = string.IsNullOrWhiteSpace(suggested) ? "download" : Path.GetFileName(suggested);
                    using var dlg = new SaveFileDialog
                    {
                        FileName = suggestedName,
                        Title = "İndirme konumunu seç",
                        OverwritePrompt = true,
                        RestoreDirectory = true,
                        Filter = "Tüm dosyalar (*.*)|*.*"
                    };
                    try
                    {
                        var dir = Path.GetDirectoryName(suggested);
                        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir)) dlg.InitialDirectory = dir;
                    }
                    catch { }

                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        e.ResultFilePath = dlg.FileName;
                        // e.Handled = false; // allow download to proceed to chosen path
                        NotifyWeb(new { type = "download-started", path = dlg.FileName, file = Path.GetFileName(dlg.FileName) });
                        var op = e.DownloadOperation;
                        op.BytesReceivedChanged += (_, __) => ReportDownloadProgress(op, dlg.FileName);
                        op.EstimatedEndTimeChanged += (_, __) => ReportDownloadProgress(op, dlg.FileName);
                        op.StateChanged += (_, __) =>
                        {
                            ReportDownloadProgress(op, dlg.FileName);
                            try
                            {
                                if (op.State == CoreWebView2DownloadState.Completed)
                                    NotifyWeb(new { type = "download-completed", path = dlg.FileName, file = Path.GetFileName(dlg.FileName) });
                                else if (op.State == CoreWebView2DownloadState.Interrupted)
                                    NotifyWeb(new { type = "download-interrupted", path = dlg.FileName, file = Path.GetFileName(dlg.FileName), reason = op.InterruptReason.ToString() });
                            }
                            catch { }
                        };
                    }
                    else
                    {
                        e.Cancel = true;
                        NotifyWeb(new { type = "download-cancelled" });
                    }
                }
                catch (Exception ex)
                {
                    try { e.Cancel = true; } catch { }
                    NotifyWeb(new { type = "download-interrupted", reason = ex.Message });
                }
                finally
                {
                    try { deferral.Complete(); } catch { }
                }
            }));
        }
        catch
        {
            // fallback: let default behavior happen
        }
    }

    private void ReportDownloadProgress(CoreWebView2DownloadOperation op, string path)
    {
        try
        {
            long received = 0, total = 0;
            try { received = (long)op.BytesReceived; } catch { }
            try { total = (long)op.TotalBytesToReceive; } catch { }
            double percent = total > 0 ? Math.Max(0, Math.Min(100, (received * 100.0) / total)) : -1;
            NotifyWeb(new { type = "download-progress", path, file = Path.GetFileName(path), received, total, percent });
        }
        catch { }
    }

    private void NotifyWeb(object payload)
    {
        try
        {
            if (webView?.CoreWebView2 == null) return;
            var json = JsonSerializer.Serialize(payload);
            if (InvokeRequired) BeginInvoke(new Action(() => { try { webView.CoreWebView2.PostWebMessageAsString(json); } catch { } }));
            else webView.CoreWebView2.PostWebMessageAsString(json);
        }
        catch { }
    }

    private void StartServer()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        var exe = Path.Combine(dir, "VioletStudio.Web.exe");
        if (!File.Exists(exe)) throw new FileNotFoundException("VioletStudio.Web.exe bulunamadı.", exe);
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = dir,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.EnvironmentVariables["VIOLET_DESKTOP"] = "1";
        // VIOLET_OPEN_BROWSER intentionally not set
        server = Process.Start(psi);
    }

    private static async Task WaitForServer(string url)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        for (int i = 0; i < 60; i++)
        {
            try
            {
                using var r = await http.GetAsync(url);
                if ((int)r.StatusCode < 500) return;
            }
            catch { }
            await Task.Delay(500);
        }
        throw new TimeoutException("Sunucu zamanında yanıt vermedi.");
    }
}
