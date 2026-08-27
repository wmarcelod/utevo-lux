using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UtevoLux.Features.Map;

/// <summary>
/// Reads the installed Tibia client's <c>minimapmarkers.bin</c> (the player's own in-game map
/// marks) into <see cref="MapMarker"/> records so the map can show them as a read-only overlay and
/// optionally import them into the editable pins.
///
/// FORMAT: the file is a bare protobuf stream of repeated <c>Marker</c> messages (field 1,
/// length-delimited). Reverse-engineered from a live install (no .proto ships with the client):
/// <code>
///   Marker   { Position position = 1; uint32 icon = 2; string description = 3; uint32 unused = 4; }
///   Position { uint32 x = 1; uint32 y = 2; uint32 z = 3; }
/// </code>
/// icon is 0..19, matching the 20 <c>Resources/Icons/MapMarkers/marker_NN.png</c> icons. Everything
/// is best-effort and never throws: on any failure (no install, unreadable, malformed) it returns
/// the last good parse or an empty list, and the caller falls back to showing no game marks.
/// The parse is memoized against the file's last-write time so re-reads are cheap.
/// </summary>
public static class GameMarkerReader
{
    private static readonly object Gate = new object();
    private static IReadOnlyList<MapMarker>? _cache;
    private static string? _cachePath;
    private static long _cacheStampTicks;

    /// <summary>The player's game marks, or an empty list when no readable file is found. Never throws.</summary>
    public static IReadOnlyList<MapMarker> Load()
    {
        string? path = GameMinimapLocator.FindPlayerMarkersFile();
        if (path == null)
            return Array.Empty<MapMarker>();

        try
        {
            long stamp = File.GetLastWriteTimeUtc(path).Ticks;
            lock (Gate)
            {
                if (_cache != null && _cachePath == path && _cacheStampTicks == stamp)
                    return _cache;
            }

            byte[] data = File.ReadAllBytes(path);
            List<MapMarker> parsed = Parse(data);

            lock (Gate)
            {
                _cache = parsed;
                _cachePath = path;
                _cacheStampTicks = stamp;
            }
            return parsed;
        }
        catch
        {
            lock (Gate)
            {
                return _cache ?? (IReadOnlyList<MapMarker>)Array.Empty<MapMarker>();
            }
        }
    }

    // ---------------------------------------------------------------- minimal protobuf parse

    private static List<MapMarker> Parse(byte[] b)
    {
        var result = new List<MapMarker>();
        int i = 0;
        while (i < b.Length)
        {
            if (!ReadKey(b, ref i, out int field, out int wire))
                break;
            if (field == 1 && wire == 2)
            {
                int len = (int)ReadVarint(b, ref i);
                int end = Math.Min(i + Math.Max(len, 0), b.Length);
                MapMarker? m = ParseMarker(b, i, end);
                if (m != null)
                    result.Add(m);
                i = end;
            }
            else
            {
                SkipField(b, ref i, wire);
            }
        }
        return result;
    }

    private static MapMarker? ParseMarker(byte[] b, int i, int end)
    {
        int x = 0, y = 0, z = 0, icon = 0;
        string desc = "";
        while (i < end)
        {
            if (!ReadKey(b, ref i, out int field, out int wire))
                break;
            if (field == 1 && wire == 2) // position submessage
            {
                int len = (int)ReadVarint(b, ref i);
                int pend = Math.Min(i + Math.Max(len, 0), end);
                while (i < pend)
                {
                    if (!ReadKey(b, ref i, out int pf, out int pw))
                        break;
                    if (pw == 0)
                    {
                        long v = ReadVarint(b, ref i);
                        if (pf == 1) x = (int)v;
                        else if (pf == 2) y = (int)v;
                        else if (pf == 3) z = (int)v;
                    }
                    else
                    {
                        SkipField(b, ref i, pw);
                    }
                }
                i = pend;
            }
            else if (field == 2 && wire == 0)
            {
                icon = (int)ReadVarint(b, ref i);
            }
            else if (field == 3 && wire == 2)
            {
                int len = (int)ReadVarint(b, ref i);
                int e = Math.Min(i + Math.Max(len, 0), end);
                desc = Encoding.UTF8.GetString(b, i, e - i);
                i = e;
            }
            else
            {
                SkipField(b, ref i, wire);
            }
        }

        if (x == 0 && y == 0)
            return null; // no usable position

        return new MapMarker
        {
            X = x,
            Y = y,
            Z = z,
            Icon = (icon >= 0 && icon < MapMarker.IconCount) ? icon : 0,
            Description = desc,
            IsSaved = false
        };
    }

    private static long ReadVarint(byte[] b, ref int i)
    {
        long val = 0;
        int shift = 0;
        while (i < b.Length && shift < 64)
        {
            byte x = b[i++];
            val |= (long)(x & 0x7f) << shift;
            if ((x & 0x80) == 0)
                break;
            shift += 7;
        }
        return val;
    }

    private static bool ReadKey(byte[] b, ref int i, out int field, out int wire)
    {
        field = 0;
        wire = 0;
        if (i >= b.Length)
            return false;
        long key = ReadVarint(b, ref i);
        field = (int)(key >> 3);
        wire = (int)(key & 7);
        return true;
    }

    private static void SkipField(byte[] b, ref int i, int wire)
    {
        switch (wire)
        {
            case 0: ReadVarint(b, ref i); break;             // varint
            case 2: i += (int)ReadVarint(b, ref i); break;   // length-delimited
            case 5: i += 4; break;                           // 32-bit
            case 1: i += 8; break;                           // 64-bit
            default: i = b.Length; break;                    // unknown/group -> stop
        }
    }
}
