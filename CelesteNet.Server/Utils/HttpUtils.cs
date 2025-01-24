using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using Size = System.Drawing.Size;
using SixLabors.ImageSharp.Processing;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Celeste.Mod.CelesteNet.Server.Utils;

internal static class HttpUtils
{
    private static HttpClient httpClient;

    static HttpUtils()
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        HttpClientHandler handler = new();
        handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        httpClient = new(handler);
        httpClient.Timeout = TimeSpan.FromSeconds(7);
    }

    public static string Post(string url, string content)
    {
        HttpContent httpContent = new StringContent(content, Encoding.UTF8);
        httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/json; charset=utf-8");
        httpContent.Headers.Add("celestenet", "nayNet");
        var response = httpClient.PostAsync(url, httpContent).Result;
        response.EnsureSuccessStatusCode();
        return response.Content.ReadAsStringAsync().Result;
    }

    public static string Get(string url)
    {
        HttpRequestMessage reqMsg = new(HttpMethod.Get, url);
        var res = httpClient.SendAsync(reqMsg).Result;
        return res.Content.ReadAsStringAsync().Result;
    }

    public static string GetImage(string url, string fileName)
    {
        if (!Directory.Exists("temp"))
            Directory.CreateDirectory("temp");

        var netStream = httpClient.GetStreamAsync(url).Result;

        using MemoryStream memoryStream = new();
        netStream.CopyTo(memoryStream);
        memoryStream.Seek(0, SeekOrigin.Begin);
        using Image image = Image.Load(memoryStream);
        image.Mutate(p =>
        {
            p.Resize(64, 64);
        });
        string filePath = Path.Combine("temp", $"{fileName}.png");
        image.SaveAsPng(filePath);
        Logger.Log(LogLevel.VVV, "Avatar", $"Cache created for {filePath}");
        return filePath;
    }
}
