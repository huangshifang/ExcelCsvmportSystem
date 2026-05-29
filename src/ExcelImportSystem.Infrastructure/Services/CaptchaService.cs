using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using ExcelImportSystem.Core.Interfaces;

namespace ExcelImportSystem.Infrastructure.Services;

public class CaptchaService : ICaptchaService
{
    private static readonly ConcurrentDictionary<string, (string Code, DateTime Expires)> Store = new();
    private static readonly char[] Chars = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ".ToCharArray();
    private const int TtlMinutes = 5;

    public CaptchaService(ILogger<CaptchaService> logger) { }

    public (string Token, string Base64Image) Generate()
    {
        var code = GenerateCode(4);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        Store[token] = (code, DateTime.UtcNow.AddMinutes(TtlMinutes));
        CleanExpired();

        var svg = RenderSvg(code);
        return (token, Convert.ToBase64String(Encoding.UTF8.GetBytes(svg)));
    }

    public bool Validate(string token, string code)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(code))
            return false;

        if (!Store.TryRemove(token, out var entry))
            return false;

        if (DateTime.UtcNow > entry.Expires)
            return false;

        return string.Equals(entry.Code, code, StringComparison.OrdinalIgnoreCase);
    }

    private static string GenerateCode(int length)
    {
        var buf = new char[length];
        for (int i = 0; i < length; i++)
            buf[i] = Chars[RandomNumberGenerator.GetInt32(Chars.Length)];
        return new string(buf);
    }

    private static string RenderSvg(string code)
    {
        var rng = Random.Shared;
        var width = 140;
        var height = 50;

        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\">");

        // Background
        sb.Append($"<rect width=\"{width}\" height=\"{height}\" fill=\"#f8f8f8\"/>");

        // Noise dots
        for (int i = 0; i < 40; i++)
        {
            var cx = rng.Next(width);
            var cy = rng.Next(height);
            var r = rng.Next(1, 3);
            var gray = rng.Next(160, 220);
            sb.Append($"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{r}\" fill=\"rgb({gray},{gray},{gray})\"/>");
        }

        // Interfering lines
        for (int i = 0; i < 3; i++)
        {
            var x1 = rng.Next(0, 40);
            var y1 = rng.Next(0, height);
            var x2 = rng.Next(100, width);
            var y2 = rng.Next(0, height);
            var gray = rng.Next(170, 210);
            sb.Append($"<line x1=\"{x1}\" y1=\"{y1}\" x2=\"{x2}\" y2=\"{y2}\" stroke=\"rgb({gray},{gray},{gray})\" stroke-width=\"1.5\"/>");
        }

        // Characters with rotation and color variation
        for (int i = 0; i < code.Length; i++)
        {
            var ch = code[i];
            var x = 18 + i * 30 + rng.Next(-3, 4);
            var y = 36 + rng.Next(-5, 6);
            var angle = rng.Next(-30, 31);
            var size = 26 + rng.Next(0, 5);
            var r = rng.Next(0, 80);
            var g = rng.Next(0, 80);
            var b = rng.Next(120, 230);

            sb.Append($"<text x=\"{x}\" y=\"{y}\" font-size=\"{size}\" font-weight=\"bold\" ");
            sb.Append($"font-family=\"Arial, sans-serif\" fill=\"rgb({r},{g},{b})\" ");
            sb.Append($"transform=\"rotate({angle}, {x + 10}, {y - 10})\">{ch}</text>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static void CleanExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in Store)
        {
            if (now > kv.Value.Expires)
                Store.TryRemove(kv.Key, out _);
        }
    }
}
