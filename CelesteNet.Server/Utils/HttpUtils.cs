using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using Size = System.Drawing.Size;
using SixLabors.ImageSharp.Processing;

namespace Celeste.Mod.CelesteNet.Server.Utils
{
    // TODO 使用 HttpClient 而不是过时的 WebRequest
    internal class HttpUtils
    {
        public static string Post(string url, string content)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            string result = "";
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json; charset=utf-8";
            req.Headers.Add("celestenet", "nayNet");
            byte[] data = Encoding.UTF8.GetBytes(content);
            req.ContentLength = data.Length;
            using (Stream reqStream = req.GetRequestStream())
            {
                reqStream.Write(data, 0, data.Length);
                reqStream.Close();
            }

            HttpWebResponse resp = (HttpWebResponse)req.GetResponse();
            Stream stream = resp.GetResponseStream();
            //获取响应内容
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                result = reader.ReadToEnd();
            }
            return result;
        }

        public static string Get(string url, int Timeout)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.UserAgent = null;
            request.Timeout = Timeout;
            request.KeepAlive = true;
            request.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;
            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            Stream myResponseStream = response.GetResponseStream();
            StreamReader myStreamReader = new StreamReader(myResponseStream, Encoding.GetEncoding("utf-8"));
            string retString = myStreamReader.ReadToEnd();
            myStreamReader.Close();
            myResponseStream.Close();

            return retString;
        }

        public static string GetImage(string url, string fileName)
        {
            if (!Directory.Exists("temp"))
            {
                Directory.CreateDirectory("temp");
            }
            string filePath = "temp/" + fileName + ".png";

            if (File.Exists(filePath))
            {
                Logger.Log(LogLevel.VVV, "Avater", "Cache Exists");
                return filePath;
            }

            WebRequest webRequest = WebRequest.Create(url);
            WebResponse webResponse = webRequest.GetResponse();
            Stream myStream = webResponse.GetResponseStream();
            Image image = Image.Load(myStream);
            image = ResizeImage(image, new Size(64, 64));
            image.SaveAsPng(filePath);
            Logger.Log(LogLevel.VVV, "Avater", "Cache Created");
            return filePath;
        }

        private static Image ResizeImage(Image imgToResize, Size size)
        {
            Image newImage = imgToResize;
            newImage.Mutate(p =>
            {
                p.Resize(size.Width,size.Height);
            });
            return newImage;
        }

    }
}
