using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UtevoLux.Features.Map;

/// <summary>
/// Encodes/decodes shareable marker (<c>TV-</c>) and route (<c>TVR-</c>) codes. Markers are
/// url-safe base64 JSON; routes are a compact zig-zag varint delta binary (v2) with a legacy
/// JSON (v1) decode path. All decodes are bounds- and length-checked and return a typed
/// success/error result.
/// </summary>
public static class ShareCodeService
{
    public sealed class ShareCodeResult
    {
        public bool Success { get; init; }

        public MapMarker? Marker { get; init; }

        public string? Error { get; init; }

        public static ShareCodeResult Ok(MapMarker marker)
        {
            return new ShareCodeResult
            {
                Success = true,
                Marker = marker
            };
        }

        public static ShareCodeResult Fail(string error)
        {
            return new ShareCodeResult
            {
                Success = false,
                Error = error
            };
        }
    }

    private sealed class Payload
    {
        [JsonPropertyName("v")]
        public int? V { get; set; }

        [JsonPropertyName("x")]
        public int? X { get; set; }

        [JsonPropertyName("y")]
        public int? Y { get; set; }

        [JsonPropertyName("z")]
        public int? Z { get; set; }

        [JsonPropertyName("i")]
        public int? I { get; set; }

        [JsonPropertyName("d")]
        public string? D { get; set; }
    }

    public sealed class RouteCodeResult
    {
        public bool Success { get; init; }

        public MapRoute? Route { get; init; }

        public string? Error { get; init; }

        public static RouteCodeResult Ok(MapRoute route)
        {
            return new RouteCodeResult
            {
                Success = true,
                Route = route
            };
        }

        public static RouteCodeResult Fail(string error)
        {
            return new RouteCodeResult
            {
                Success = false,
                Error = error
            };
        }
    }

    private sealed class RoutePayload
    {
        [JsonPropertyName("v")]
        public int? V { get; set; }

        [JsonPropertyName("n")]
        public string? N { get; set; }

        [JsonPropertyName("p")]
        public List<List<int>>? P { get; set; }
    }

    public const string Prefix = "TV-";

    public const string RoutePrefix = "TVR-";

    public const int FormatVersion = 1;

    private const int MaxCodeLength = 4096;

    private const int MaxRouteCodeLength = 8192;

    private const byte RouteBinaryMarker = 2;

    private const string RouteInvalidMessage = "That doesn't look like a valid route code.";

    private const string RouteOutsideMapMessage = "This route goes outside the map.";

    public static string Encode(MapMarker marker)
    {
        if (marker == null)
        {
            throw new ArgumentNullException("marker");
        }
        string s = JsonSerializer.Serialize(new Payload
        {
            V = 1,
            X = marker.X,
            Y = marker.Y,
            Z = marker.Z,
            I = marker.Icon,
            D = (marker.Description ?? "")
        });
        string text = Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).Replace('+', '-').Replace('/', '_')
            .TrimEnd('=');
        return "TV-" + text;
    }

    public static ShareCodeResult TryDecode(string input, MapBounds bounds)
    {
        try
        {
            return DecodeCore(input, bounds);
        }
        catch
        {
            return ShareCodeResult.Fail("That code could not be read.");
        }
    }

    private static ShareCodeResult DecodeCore(string input, MapBounds bounds)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return ShareCodeResult.Fail("Please enter a code.");
        }
        string text = input.Trim();
        if (text.StartsWith("TV-", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring("TV-".Length);
        }
        if (text.Length == 0 || text.Length > 4096)
        {
            return ShareCodeResult.Fail("That doesn't look like a valid share code.");
        }
        string text2 = text.Replace('-', '+').Replace('_', '/');
        int num = text2.Length % 4;
        if (num == 1)
        {
            return ShareCodeResult.Fail("That doesn't look like a valid share code.");
        }
        if (num > 0)
        {
            text2 += new string('=', 4 - num);
        }
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(text2);
        }
        catch
        {
            return ShareCodeResult.Fail("That doesn't look like a valid share code.");
        }
        Payload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(Encoding.UTF8.GetString(bytes));
        }
        catch
        {
            return ShareCodeResult.Fail("That doesn't look like a valid share code.");
        }
        if (payload == null || !payload.V.HasValue)
        {
            return ShareCodeResult.Fail("That doesn't look like a valid share code.");
        }
        if (payload.V != 1)
        {
            return ShareCodeResult.Fail("This code needs a newer version of UtevoLux.");
        }
        if (!payload.X.HasValue || !payload.Y.HasValue || !payload.Z.HasValue || !payload.I.HasValue)
        {
            return ShareCodeResult.Fail("That doesn't look like a valid share code.");
        }
        if (payload.Z < 0 || payload.Z >= 16)
        {
            return ShareCodeResult.Fail("This code points outside the map.");
        }
        if (!bounds.Contains(payload.X.Value, payload.Y.Value))
        {
            return ShareCodeResult.Fail("This code points outside the map.");
        }
        if (payload.I < 0 || payload.I >= 20)
        {
            return ShareCodeResult.Fail("That doesn't look like a valid share code.");
        }
        return ShareCodeResult.Ok(new MapMarker
        {
            Id = Guid.NewGuid(),
            X = payload.X.Value,
            Y = payload.Y.Value,
            Z = payload.Z.Value,
            Icon = payload.I.Value,
            Description = SanitizeDescription(payload.D),
            CreatedAt = DateTime.Now
        });
    }

    public static string EncodeRoute(MapRoute route)
    {
        if (route == null)
        {
            throw new ArgumentNullException("route");
        }
        List<RoutePoint> list = route.Points.Take(100).ToList();
        byte[] bytes = Encoding.UTF8.GetBytes(SanitizeText(route.Name, 40));
        using MemoryStream memoryStream = new MemoryStream();
        memoryStream.WriteByte(2);
        memoryStream.WriteByte((byte)bytes.Length);
        memoryStream.Write(bytes, 0, bytes.Length);
        memoryStream.WriteByte((byte)list.Count);
        if (list.Count > 0)
        {
            RoutePoint routePoint = list[0];
            memoryStream.WriteByte((byte)(routePoint.X & 0xFF));
            memoryStream.WriteByte((byte)((routePoint.X >> 8) & 0xFF));
            memoryStream.WriteByte((byte)(routePoint.Y & 0xFF));
            memoryStream.WriteByte((byte)((routePoint.Y >> 8) & 0xFF));
            memoryStream.WriteByte((byte)routePoint.Z);
            for (int i = 1; i < list.Count; i++)
            {
                WriteVarUInt(memoryStream, ZigZag(list[i].X - list[i - 1].X));
                WriteVarUInt(memoryStream, ZigZag(list[i].Y - list[i - 1].Y));
                WriteVarUInt(memoryStream, ZigZag(list[i].Z - list[i - 1].Z));
            }
        }
        string text = Convert.ToBase64String(memoryStream.ToArray());
        return "TVR-" + text.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public static RouteCodeResult TryDecodeRoute(string input, MapBounds bounds)
    {
        try
        {
            return DecodeRouteCore(input, bounds);
        }
        catch
        {
            return RouteCodeResult.Fail("That code could not be read.");
        }
    }

    private static RouteCodeResult DecodeRouteCore(string input, MapBounds bounds)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return RouteCodeResult.Fail("Please enter a code.");
        }
        string text = input.Trim();
        if (text.StartsWith("TVR-", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring("TVR-".Length);
        }
        if (text.Length == 0 || text.Length > 8192)
        {
            return RouteCodeResult.Fail("That doesn't look like a valid route code.");
        }
        string text2 = text.Replace('-', '+').Replace('_', '/');
        int num = text2.Length % 4;
        if (num == 1)
        {
            return RouteCodeResult.Fail("That doesn't look like a valid route code.");
        }
        if (num > 0)
        {
            text2 += new string('=', 4 - num);
        }
        byte[] array;
        try
        {
            array = Convert.FromBase64String(text2);
        }
        catch
        {
            return RouteCodeResult.Fail("That doesn't look like a valid route code.");
        }
        if (array.Length == 0)
        {
            return RouteCodeResult.Fail("That doesn't look like a valid route code.");
        }
        if (array[0] == 123)
        {
            return DecodeRouteV1Json(array, bounds);
        }
        if (array[0] == 2)
        {
            return DecodeRouteV2Binary(array, bounds);
        }
        return RouteCodeResult.Fail("That doesn't look like a valid route code.");
    }

    private static RouteCodeResult DecodeRouteV1Json(byte[] bytes, MapBounds bounds)
    {
        RoutePayload? routePayload;
        try
        {
            routePayload = JsonSerializer.Deserialize<RoutePayload>(Encoding.UTF8.GetString(bytes));
        }
        catch
        {
            return RouteCodeResult.Fail("That doesn't look like a valid route code.");
        }
        if (routePayload == null || !routePayload.V.HasValue)
        {
            return RouteCodeResult.Fail("That doesn't look like a valid route code.");
        }
        if (routePayload.V != 1)
        {
            return RouteCodeResult.Fail("This code needs a newer version of UtevoLux.");
        }
        if (routePayload.P == null || routePayload.P.Count < 2 || routePayload.P.Count > 100)
        {
            return RouteCodeResult.Fail("That doesn't look like a valid route code.");
        }
        List<RoutePoint> list = new List<RoutePoint>();
        foreach (List<int> item in routePayload.P)
        {
            if (item == null || item.Count != 3)
            {
                return RouteCodeResult.Fail("That doesn't look like a valid route code.");
            }
            if (item[2] < 0 || item[2] >= 16 || !bounds.Contains(item[0], item[1]))
            {
                return RouteCodeResult.Fail("This route goes outside the map.");
            }
            list.Add(new RoutePoint(item[0], item[1], item[2]));
        }
        return BuildRoute(routePayload.N, list);
    }

    private static RouteCodeResult DecodeRouteV2Binary(byte[] data, MapBounds bounds)
    {
        int num = 1;
        if (num >= data.Length)
        {
            return RouteCodeResult.Fail("That doesn't look like a valid route code.");
        }
        int num2 = data[num++];
        if (num2 > 160 || num + num2 > data.Length)
        {
            return RouteCodeResult.Fail("That doesn't look like a valid route code.");
        }
        string name;
        try
        {
            name = Encoding.UTF8.GetString(data, num, num2);
        }
        catch
        {
            return RouteCodeResult.Fail("That doesn't look like a valid route code.");
        }
        num += num2;
        if (num >= data.Length)
        {
            return RouteCodeResult.Fail("That doesn't look like a valid route code.");
        }
        int num3 = data[num++];
        if (num3 < 2 || num3 > 100)
        {
            return RouteCodeResult.Fail("That doesn't look like a valid route code.");
        }
        if (num + 5 > data.Length)
        {
            return RouteCodeResult.Fail("That doesn't look like a valid route code.");
        }
        int num4 = data[num] | (data[num + 1] << 8);
        int num5 = data[num + 2] | (data[num + 3] << 8);
        int num6 = data[num + 4];
        num += 5;
        List<RoutePoint> list = new List<RoutePoint>(num3);
        if (num6 >= 16 || !bounds.Contains(num4, num5))
        {
            return RouteCodeResult.Fail("This route goes outside the map.");
        }
        list.Add(new RoutePoint(num4, num5, num6));
        for (int i = 1; i < num3; i++)
        {
            if (!TryReadVarUInt(data, ref num, out var value) || !TryReadVarUInt(data, ref num, out var value2) || !TryReadVarUInt(data, ref num, out var value3))
            {
                return RouteCodeResult.Fail("That doesn't look like a valid route code.");
            }
            num4 += UnZigZag(value);
            num5 += UnZigZag(value2);
            num6 += UnZigZag(value3);
            if (num6 < 0 || num6 >= 16 || !bounds.Contains(num4, num5))
            {
                return RouteCodeResult.Fail("This route goes outside the map.");
            }
            list.Add(new RoutePoint(num4, num5, num6));
        }
        if (num != data.Length)
        {
            return RouteCodeResult.Fail("That doesn't look like a valid route code.");
        }
        return BuildRoute(name, list);
    }

    private static RouteCodeResult BuildRoute(string? name, List<RoutePoint> points)
    {
        return RouteCodeResult.Ok(new MapRoute
        {
            Id = Guid.NewGuid(),
            Name = SanitizeText(name, 40),
            Points = points,
            CreatedAt = DateTime.Now
        });
    }

    private static uint ZigZag(int value)
    {
        return (uint)((value << 1) ^ (value >> 31));
    }

    private static int UnZigZag(uint value)
    {
        return (int)((value >> 1) ^ (0 - (value & 1)));
    }

    private static void WriteVarUInt(MemoryStream ms, uint value)
    {
        while (value >= 128)
        {
            ms.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        ms.WriteByte((byte)value);
    }

    private static bool TryReadVarUInt(byte[] data, ref int pos, out uint value)
    {
        value = 0u;
        int num = 0;
        while (true)
        {
            if (pos >= data.Length || num > 28)
            {
                return false;
            }
            byte b = data[pos++];
            value |= (uint)((b & 0x7F) << num);
            if ((b & 0x80) == 0)
            {
                break;
            }
            num += 7;
        }
        return true;
    }

    public static string SanitizeDescription(string? description)
    {
        return SanitizeText(description, 100);
    }

    public static string SanitizeText(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || maxLength <= 0)
        {
            return "";
        }
        StringBuilder stringBuilder = new StringBuilder(Math.Min(text.Length, maxLength));
        foreach (char c in text)
        {
            if (!char.IsControl(c))
            {
                if (stringBuilder.Length >= maxLength)
                {
                    break;
                }
                stringBuilder.Append(c);
            }
        }
        if (stringBuilder.Length > 0 && char.IsHighSurrogate(stringBuilder[stringBuilder.Length - 1]))
        {
            stringBuilder.Length--;
        }
        return stringBuilder.ToString().Trim();
    }
}
