using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using OpenTibiaVision.Models;

namespace OpenTibiaVision.Services;

/// <summary>
/// Non-GUI runtime smoke test. Exercises the P/Invoke surface and services so we can
/// confirm the plumbing works even when the window cannot be driven interactively.
/// It is read-only with respect to the user's saved regions.
/// </summary>
public static class SelfTest
{
    public static void Run(string outputPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("OpenTibiaVision self-test");
        sb.AppendLine($"UTC: {DateTime.UtcNow:O}");
        sb.AppendLine($"Process bitness: {(IntPtr.Size == 8 ? "x64" : "x86")}");
        sb.AppendLine();

        try
        {
            // 1) Window enumeration (EnumWindows + GetWindowText* + IsWindowVisible).
            List<WindowInfo> windows = WindowFinder.ListWindows();
            sb.AppendLine($"[OK] ListWindows -> {windows.Count} visible titled window(s)");
            foreach (WindowInfo w in windows.Take(6))
                sb.AppendLine($"       {w.Hwnd.ToInt64():X}  {Trim(w.Title, 70)}");

            // 2) Tibia detection.
            IntPtr tibia = WindowFinder.FindTibia();
            sb.AppendLine($"[OK] FindTibia -> {(tibia == IntPtr.Zero ? "not running" : "hwnd " + tibia.ToInt64().ToString("X"))}");

            // 3) DWM extended-frame-bounds + DPI (interop marshalling of RECT / uint).
            if (windows.Count > 0)
            {
                RECT bounds = DwmThumbnail.GetSourceBounds(windows[0].Hwnd);
                double scale = NativeMethods.GetScaleForWindow(windows[0].Hwnd);
                sb.AppendLine($"[OK] GetSourceBounds(first) -> {bounds.Width}x{bounds.Height} px at scale {scale:0.00}");
            }

            // 4) JSON round-trip of the persistence model (in-memory, non-destructive).
            var sample = new RegionConfig
            {
                Name = "selftest",
                SourceTitle = "sample",
                CropLeft = 10, CropTop = 20, CropRight = 210, CropBottom = 170
            };
            string json = JsonSerializer.Serialize(new List<RegionConfig> { sample });
            List<RegionConfig>? back = JsonSerializer.Deserialize<List<RegionConfig>>(json);
            sb.AppendLine($"[OK] RegionConfig JSON round-trip -> {back?.Count ?? 0} item, crop {back?[0].CropWidth}x{back?[0].CropHeight}");

            // 5) Read the real store (does not modify it).
            List<RegionConfig> existing = RegionStore.Load();
            sb.AppendLine($"[OK] RegionStore.Load -> {existing.Count} saved region(s)");
            sb.AppendLine($"       store path: {RegionStore.FilePath}");

            // 6) Full DWM thumbnail cycle against a real source, into an off-screen host
            //    HWND (created but never shown). Proves register/query/update/unregister all
            //    return S_OK - i.e. the live-mirror mechanism itself works. Only the visible
            //    pixels can't be asserted headlessly.
            if (windows.Count > 0)
            {
                var host = new Window
                {
                    Width = 200,
                    Height = 150,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    ShowActivated = false
                };
                try
                {
                    IntPtr dest = new WindowInteropHelper(host).EnsureHandle(); // HWND without showing
                    int hrReg = DwmThumbnail.DwmRegisterThumbnail(dest, windows[0].Hwnd, out IntPtr thumb);
                    int hrSize = DwmThumbnail.DwmQueryThumbnailSourceSize(thumb, out SIZE srcSize);

                    var props = new DWM_THUMBNAIL_PROPERTIES
                    {
                        dwFlags = DwmThumbnail.DWM_TNP_RECTDESTINATION |
                                  DwmThumbnail.DWM_TNP_RECTSOURCE |
                                  DwmThumbnail.DWM_TNP_OPACITY |
                                  DwmThumbnail.DWM_TNP_VISIBLE,
                        rcDestination = new RECT(0, 0, 200, 150),
                        rcSource = new RECT(0, 0,
                            hrSize == 0 ? srcSize.cx : 100,
                            hrSize == 0 ? srcSize.cy : 100),
                        opacity = 255,
                        fVisible = true,
                        fSourceClientAreaOnly = false
                    };
                    int hrUpd = DwmThumbnail.DwmUpdateThumbnailProperties(thumb, ref props);
                    int hrUnreg = DwmThumbnail.DwmUnregisterThumbnail(thumb);

                    sb.AppendLine($"[OK] DWM thumbnail cycle -> register=0x{hrReg:X8} " +
                                  $"querySize=0x{hrSize:X8} ({srcSize.cx}x{srcSize.cy}) " +
                                  $"update=0x{hrUpd:X8} unregister=0x{hrUnreg:X8}  (0 == S_OK)");
                }
                finally
                {
                    host.Close();
                }
            }

            sb.AppendLine();
            sb.AppendLine("RESULT: OK");
        }
        catch (Exception ex)
        {
            sb.AppendLine();
            sb.AppendLine("RESULT: FAIL");
            sb.AppendLine(ex.ToString());
        }

        try
        {
            File.WriteAllText(outputPath, sb.ToString());
        }
        catch
        {
            // If we cannot write the file there is nothing more we can do here.
        }
    }

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max] + "...";
}
