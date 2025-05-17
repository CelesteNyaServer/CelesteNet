using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;

namespace Celeste.Mod.CelesteNet.Client.Utils
{
    public class AvatarManager
    {
        private static readonly HttpClient httpClient;
        private static readonly string CacheDirectory;
        private const int AvatarSize = 64;
        
        // 文件操作锁，防止并发访问
        private static readonly object FileLock = new object();
        
        // 读取中的头像缓存，避免重复读取
        private static readonly HashSet<string> ProcessingAvatars = new HashSet<string>();

        // URL到MD5缓存的映射
        private static readonly Dictionary<string, string> UrlToMd5Cache = new Dictionary<string, string>();
        
        private readonly Dictionary<string, DateTime> CacheTimestamps = new Dictionary<string, DateTime>();
        private readonly CelesteNetClientContext Context;

        static AvatarManager()
        {
            httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            // 设置缓存目录，确保它存在于 Celeste 的保存目录中
            try 
            {
                // 使用相对目录
                CacheDirectory = Path.Combine("CelesteNet", "AvatarCache");
                Directory.CreateDirectory(CacheDirectory);
                Logger.Log(LogLevel.INF, "avatar", $"创建头像缓存目录: {CacheDirectory}");
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.ERR, "avatar", $"创建缓存目录时出错: {ex.Message}");
                // 如果无法创建目录，使用临时目录
                string tempPath = Path.Combine(Path.GetTempPath(), "CelesteNet", "AvatarCache");
                Directory.CreateDirectory(tempPath);
                CacheDirectory = tempPath;
                Logger.Log(LogLevel.INF, "avatar", $"使用临时缓存目录: {CacheDirectory}");
            }
        }

        public AvatarManager(CelesteNetClientContext context)
        {
            Context = context;
            LoadCacheTimestamps();
        }

        // 计算URL的MD5哈希值
        private string GetMd5Hash(string url)
        {
            if (UrlToMd5Cache.TryGetValue(url, out string cachedMd5))
                return cachedMd5;
                
            using (MD5 md5 = MD5.Create())
            {
                byte[] inputBytes = Encoding.UTF8.GetBytes(url);
                byte[] hashBytes = md5.ComputeHash(inputBytes);

                // 转换为十六进制字符串
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    sb.Append(hashBytes[i].ToString("x2"));
                }
                
                string md5Hash = sb.ToString();
                UrlToMd5Cache[url] = md5Hash;
                return md5Hash;
            }
        }

        private void LoadCacheTimestamps()
        {
            try
            {
                if (!Directory.Exists(CacheDirectory))
                {
                    Logger.Log(LogLevel.INF, "avatar", $"缓存目录不存在，将创建: {CacheDirectory}");
                    Directory.CreateDirectory(CacheDirectory);
                    return;
                }

                lock (FileLock)
                {
                    string[] files = Directory.GetFiles(CacheDirectory, "*.png");
                    foreach (string file in files)
                    {
                        FileInfo info = new FileInfo(file);
                        string cacheKey = Path.GetFileNameWithoutExtension(file);
                        CacheTimestamps[cacheKey] = info.LastWriteTime;
                    }
                }
                
                Logger.Log(LogLevel.INF, "avatar", $"加载了 {CacheTimestamps.Count} 个缓存记录");
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.ERR, "avatar", $"加载缓存时间戳时出错: {ex.Message}");
            }
        }

        public async Task<bool> DownloadAndRegisterAvatarAsync(DataPlayerInfo playerInfo, string avatarUrl)
        {
            if (string.IsNullOrEmpty(avatarUrl))
                return false;

            string avatarId = $"celestenet_avatar_{playerInfo.ID}_";
            string urlMd5 = GetMd5Hash(avatarUrl);
            string cacheFile = Path.Combine(CacheDirectory, $"{urlMd5}.png");
            
            // 检查是否正在处理该头像
            lock (ProcessingAvatars)
            {
                if (ProcessingAvatars.Contains(avatarId))
                {
                    Logger.Log(LogLevel.INF, "avatar", $"头像 {avatarId} 正在处理中，跳过");
                    return false;
                }
                ProcessingAvatars.Add(avatarId);
            }
            
            try
            {
                string tempFile = Path.Combine(CacheDirectory, $"{urlMd5}_temp.png");

                // 检查是否已经有MD5缓存，且缓存不超过1天
                bool cacheExists = false;
                lock (FileLock)
                {
                    cacheExists = File.Exists(cacheFile) && 
                        CacheTimestamps.TryGetValue(urlMd5, out DateTime timestamp) && 
                        (DateTime.Now - timestamp).TotalDays < 1;
                }
                
                // 如果缓存存在，直接使用
                if (cacheExists)
                {
                    Logger.Log(LogLevel.INF, "avatar", $"使用缓存的头像: {urlMd5} 用于玩家 {playerInfo.ID}");
                    await RegisterAvatarFromFileAsync(playerInfo, cacheFile);
                    return true;
                }

                try
                {
                    // 下载头像
                    Logger.Log(LogLevel.INF, "avatar", $"下载头像: {avatarUrl} (MD5: {urlMd5})");
                    
                    using (HttpResponseMessage response = await httpClient.GetAsync(avatarUrl))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            Logger.Log(LogLevel.ERR, "avatar", $"下载头像失败: {response.StatusCode}");
                            return false;
                        }

                        // 获取响应内容
                        using (Stream stream = await response.Content.ReadAsStreamAsync())
                        {
                            // 在锁外准备内存流
                            byte[] buffer;
                            using (MemoryStream memoryStream = new MemoryStream())
                            {
                                await stream.CopyToAsync(memoryStream);
                                buffer = memoryStream.ToArray();
                            }

                            // 锁定仅用于文件操作
                            lock (FileLock)
                            {
                                // 确保不存在同名临时文件
                                if (File.Exists(tempFile))
                                    File.Delete(tempFile);
                                
                                // 同步写入文件
                                File.WriteAllBytes(tempFile, buffer);
                            }
                        }

                        // 在主线程中进行缩放操作
                        bool resized = false;
                        // 使用主线程辅助类直接在主线程上执行
                        MainThreadHelper.Schedule(() => {
                            try
                            {
                                lock (FileLock)
                                {
                                    // 缩放图像并保存到缓存
                                    ResizeImage(tempFile, cacheFile, AvatarSize, AvatarSize);
                                    resized = true;
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Log(LogLevel.ERR, "avatar", $"缩放头像时出错: {ex.Message}");
                                // 如果缩放失败，使用原始图像
                                lock (FileLock)
                                {
                                    if (File.Exists(tempFile) && !File.Exists(cacheFile))
                                        File.Copy(tempFile, cacheFile, true);
                                }
                            }
                            finally
                            {
                                // 删除临时文件
                                lock (FileLock)
                                {
                                    if (File.Exists(tempFile))
                                    {
                                        try
                                        {
                                            File.Delete(tempFile);
                                        }
                                        catch (Exception ex)
                                        {
                                            Logger.Log(LogLevel.WRN, "avatar", $"删除临时文件时出错: {ex.Message}");
                                        }
                                    }
                                }
                            }
                        });
                        
                        // 等待主线程操作完成（简单等待）
                        int waitCount = 0;
                        while (!resized && waitCount < 100) // 最多等待10秒
                        {
                            await Task.Delay(100);
                            waitCount++;
                        }
                        
                        // 确保文件处理完成后再继续
                        await Task.Delay(200);

                        bool fileExists = false;
                        lock (FileLock)
                        {
                            fileExists = File.Exists(cacheFile);
                            if (fileExists)
                            {
                                CacheTimestamps[urlMd5] = DateTime.Now;
                            }
                        }
                        
                        if (resized || fileExists)
                        {
                            // 注册头像
                            await RegisterAvatarFromFileAsync(playerInfo, cacheFile);
                            return true;
                        }
                        
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log(LogLevel.ERR, "avatar", $"处理头像时出错: {ex.Message}");
                    return false;
                }
            }
            finally
            {
                // 完成处理，从列表中移除
                lock (ProcessingAvatars)
                {
                    ProcessingAvatars.Remove(avatarId);
                }
            }
        }

        private void ResizeImage(string inputPath, string outputPath, int width, int height)
        {
            if (!File.Exists(inputPath))
            {
                Logger.Log(LogLevel.ERR, "avatar", $"缩放图像失败: 输入文件不存在 {inputPath}");
                return;
            }
            
            // 确保输出目录存在
            string? outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);
            
            // 确保输出文件不存在，避免冲突
            if (File.Exists(outputPath))
                File.Delete(outputPath);
            
            // 使用 XNA/MonoGame 的方式缩放图像
            using (FileStream fs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Texture2D originalTexture = Texture2D.FromStream(Engine.Instance.GraphicsDevice, fs);
                
                // 创建目标渲染纹理
                RenderTarget2D renderTarget = new RenderTarget2D(
                    Engine.Instance.GraphicsDevice, 
                    width, height, 
                    false, 
                    Engine.Instance.GraphicsDevice.PresentationParameters.BackBufferFormat, 
                    DepthFormat.None
                );
                
                // 将原始纹理绘制到新尺寸的渲染目标上
                Engine.Instance.GraphicsDevice.SetRenderTarget(renderTarget);
                Engine.Instance.GraphicsDevice.Clear(Color.Transparent);
                
                SpriteBatch spriteBatch = Draw.SpriteBatch;
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
                spriteBatch.Draw(
                    originalTexture, 
                    new Rectangle(0, 0, width, height), 
                    null, 
                    Color.White
                );
                spriteBatch.End();
                
                // 切换回默认渲染目标
                Engine.Instance.GraphicsDevice.SetRenderTarget(null);
                
                // 保存结果到文件
                using (FileStream outStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    renderTarget.SaveAsPng(outStream, width, height);
                    outStream.Close(); // 确保文件流关闭
                }
                
                // 释放资源
                renderTarget.Dispose();
                originalTexture.Dispose();
                fs.Close(); // 确保文件流关闭
            }
        }

        private async Task RegisterAvatarFromFileAsync(DataPlayerInfo playerInfo, string filePath)
        {
            string avatarId = $"celestenet_avatar_{playerInfo.ID}_";
            
            try
            {
                // 为了防止文件访问冲突，使用复制读取
                byte[] imageData;
                lock (FileLock)
                {
                    if (!File.Exists(filePath))
                    {
                        Logger.Log(LogLevel.ERR, "avatar", $"头像文件不存在: {filePath}");
                        return;
                    }
                    
                    imageData = File.ReadAllBytes(filePath);
                }
                
                await RegisterAvatarBytesAsync(playerInfo, imageData);
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.ERR, "avatar", $"读取头像文件时出错: {ex.Message}");
            }
        }

        public async Task RegisterAvatarBytesAsync(DataPlayerInfo playerInfo, byte[] imageData)
        {
            string avatarId = $"celestenet_avatar_{playerInfo.ID}_";
            
            // 检查客户端实例可用性
            if (Context.Client == null)
            {
                Logger.Log(LogLevel.ERR, "avatar", "客户端实例不可用");
                return;
            }
            
            // 获取 EmojiComponent
            var emojiComponent = Context.Get<Components.CelesteNetEmojiComponent>();
            if (emojiComponent == null)
            {
                Logger.Log(LogLevel.ERR, "avatar", "找不到 EmojiComponent");
                return;
            }

            // 将图像数据分片处理
            int maxSize = 1024; // 假设最大包大小为 2048 字节，分片大小为一半
            int fragmentCount = (imageData.Length + maxSize - 1) / maxSize;
            
            for (int i = 0; i < fragmentCount; i++)
            {
                int offset = i * maxSize;
                int size = Math.Min(maxSize, imageData.Length - offset);
                
                byte[] fragment = new byte[size];
                Buffer.BlockCopy(imageData, offset, fragment, 0, size);
                
                DataNetEmoji emoji = new DataNetEmoji
                {
                    ID = avatarId,
                    Data = fragment,
                    SequenceNumber = i,
                    MoreFragments = i < fragmentCount - 1
                };
                
                // 处理 emoji 分片
                await Task.Run(() => emojiComponent.Handle(Context.Client.Con, emoji));
                
                // 添加一点延迟，避免发送过快
                if (i < fragmentCount - 1)
                    await Task.Delay(10);
            }
            
            // 更新玩家显示名称
            playerInfo.DisplayName = playerInfo.DisplayName.Replace(Components.CelesteNetEmojiComponent.AvatarMissing, $":{avatarId}:");
            playerInfo.UpdateDisplayName(!Context.Client.Options.AvatarsDisabled);
        }
    }
} 