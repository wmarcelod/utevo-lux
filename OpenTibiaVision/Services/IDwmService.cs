using System;

namespace OpenTibiaVision.Services;

/// <summary>
/// High-level DWM thumbnail operations (optimization principle 1: register ONCE, then push
/// property updates; the GPU composites, the app copies zero pixels). Wraps the raw P/Invoke
/// in <see cref="DwmThumbnail"/> so feature modules receive it through IAppServices.
/// </summary>
public interface IDwmService
{
    /// <summary>Register src to be composited into dest (owned by this process). Zero on failure.</summary>
    IntPtr Register(IntPtr dest, IntPtr src);

    void Unregister(IntPtr thumb);

    /// <summary>
    /// Push destination rect (device px, client-relative), source crop, opacity and visibility.
    /// <paramref name="clientAreaOnly"/> = true crops against the source CLIENT area (game viewport).
    /// </summary>
    void Update(IntPtr thumb, RECT rcDestination, RECT rcSource, byte opacity, bool visible, bool clientAreaOnly);

    /// <summary>
    /// Opacity-only fast path: ONE byte pushed with the OPACITY flag alone — no rect recompute
    /// (optimization principle 1). Use for fades and dimming.
    /// </summary>
    void SetOpacity(IntPtr thumb, byte opacity);

    /// <summary>Full-scale source size in physical px, or empty on failure.</summary>
    SIZE QuerySourceSize(IntPtr thumb);
}

/// <summary>Default implementation over <see cref="DwmThumbnail"/>.</summary>
public sealed class DwmService : IDwmService
{
    public IntPtr Register(IntPtr dest, IntPtr src)
    {
        if (dest == IntPtr.Zero || src == IntPtr.Zero)
            return IntPtr.Zero;
        return DwmThumbnail.DwmRegisterThumbnail(dest, src, out IntPtr thumb) == 0 ? thumb : IntPtr.Zero;
    }

    public void Unregister(IntPtr thumb)
    {
        if (thumb != IntPtr.Zero)
            DwmThumbnail.DwmUnregisterThumbnail(thumb);
    }

    public void Update(IntPtr thumb, RECT rcDestination, RECT rcSource, byte opacity, bool visible, bool clientAreaOnly)
    {
        if (thumb == IntPtr.Zero)
            return;

        uint flags = DwmThumbnail.DWM_TNP_RECTDESTINATION |
                     DwmThumbnail.DWM_TNP_RECTSOURCE |
                     DwmThumbnail.DWM_TNP_OPACITY |
                     DwmThumbnail.DWM_TNP_VISIBLE |
                     DwmThumbnail.DWM_TNP_SOURCECLIENTAREAONLY;

        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = flags,
            rcDestination = rcDestination,
            rcSource = rcSource,
            opacity = opacity,
            fVisible = visible,
            fSourceClientAreaOnly = clientAreaOnly
        };
        DwmThumbnail.DwmUpdateThumbnailProperties(thumb, ref props);
    }

    public void SetOpacity(IntPtr thumb, byte opacity)
    {
        if (thumb == IntPtr.Zero)
            return;

        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DwmThumbnail.DWM_TNP_OPACITY, // opacity only: no rect recompute
            opacity = opacity
        };
        DwmThumbnail.DwmUpdateThumbnailProperties(thumb, ref props);
    }

    public SIZE QuerySourceSize(IntPtr thumb)
    {
        if (thumb != IntPtr.Zero && DwmThumbnail.DwmQueryThumbnailSourceSize(thumb, out SIZE size) == 0)
            return size;
        return default;
    }
}
