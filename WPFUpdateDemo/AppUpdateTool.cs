using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.IO.Compression;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Archives;
using HandyControl.Controls;
using System.Text;
using System.Security.Policy;

namespace WPFUpdateDemo;

public class AppUpdateTool
{
    private const string apiUrl = "https://api.github.com/repos/ManuelLau/WPFUpdateDemo/releases";
    private const string platformTag = "win-x86";
    public required MainWindow mainWindow;

    public async void UpdateApp()
    {
        if (!CheckNewVersion(out string? latestVersionString, out string? downloadUrl))
        {
            Debug.WriteLine("没有发现新的版本");
            mainWindow.outputText.Text = "当前已是最新版本";
            return;
        }
        Debug.WriteLine($"发现新版本 v{latestVersionString}");

        // 创建临时文件存放路径temp\
        var tempFileDirectory = @".\temp";
        if (!Directory.Exists(tempFileDirectory))
        {
            Directory.CreateDirectory(tempFileDirectory);
        }
        // 下载地址最后部分内容则为文件名，如果不符合规则，则使用默认文件名与格式TempFile.zip
        string tempFileName = "TempFile.zip";
        int lastIndex = downloadUrl.LastIndexOf('/');
        if (lastIndex != -1 && lastIndex < downloadUrl.Length - 1)
        {
            tempFileName = downloadUrl.Substring(lastIndex + 1);
        }
        // 下载+解压文件到temp\下
        mainWindow.outputText.Text = "正在下载 " + tempFileName;
        if (!await DownloadAndExtractFile(downloadUrl, tempFileDirectory, tempFileName))
        {
            Debug.WriteLine("下载文件失败!");
            mainWindow.outputText.Text = "下载文件失败!";
            return;
        }
        else
        {
            Debug.WriteLine("文件下载完成");
            mainWindow.outputText.Text = $"文件{tempFileName}下载完成";
        }

        // 替换旧文件，重启软件
        var currentExeFileName = Assembly.GetEntryAssembly().GetName().Name + ".exe";
        var utf8Bytes = Encoding.UTF8.GetBytes(AppContext.BaseDirectory);
        var utf8BaseDirectory = Encoding.UTF8.GetString(utf8Bytes);
        var batFilePath = Path.Combine(utf8BaseDirectory, "temp", "update.bat");
        await using (StreamWriter sw = new(batFilePath))
        {
            await sw.WriteLineAsync("@echo off");
            await sw.WriteLineAsync("chcp 65001");
            await sw.WriteLineAsync("ping 127.0.0.1 -n 3 > nul");
            var extractedPath = $"\"{utf8BaseDirectory}temp\\{Path.GetFileNameWithoutExtension(tempFileName)}\\*.*\"";
            var targetPath = $"\"{utf8BaseDirectory}\"";
            await sw.WriteLineAsync($"xcopy /E /Y {extractedPath} {targetPath}");
            await sw.WriteLineAsync($"start /d \"{utf8BaseDirectory}\" {currentExeFileName}");
            await sw.WriteLineAsync("ping 127.0.0.1 -n 1 > nul");
            await sw.WriteLineAsync($"rd /S /Q \"{utf8BaseDirectory}temp\"");
        }
        var psi = new ProcessStartInfo(batFilePath)
        {
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        Process.Start(psi);
        Application.Current.Shutdown();
    }

    public static bool CheckNewVersion(out string? latestVersionString, out string? downloadUrl)
    {
        // 获取并检查Version是否非空
        Version? localVersion = Assembly.GetExecutingAssembly().GetName().Version;
        if (!GetLatestVersionAndDownloadUrl(apiUrl, platformTag, out latestVersionString, out downloadUrl))
        {
            return false;
        }
        if (localVersion is null || string.IsNullOrEmpty(latestVersionString) || string.IsNullOrEmpty(downloadUrl))
        {
            Debug.WriteLine("LocalVersion或LatestVersionString或DownloadUrl为空");
            return false;
        }

        // 比较版本号大小
        Version latestVersion = new(RemoveFirstLetterV(latestVersionString));
        Debug.WriteLine($"LocalVersion:{localVersion} | LatestVersion:{latestVersion}");
        Debug.WriteLine("DownloadUrl:" + downloadUrl);
        if (localVersion.CompareTo(latestVersion) >= 0)
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// 通过api获取最新的版本号及其下载链接，不论是否为pre-release版本，此方法获取的json体积更小，更节省资源。
    /// <para>读取tag_name作为版本号，上传Release的时候请注意正确填写tag。</para>
    /// <para>读取browser_download_url作为下载链接，请注意上传的文件要为.zip .7z .rar等压缩文件</para>
    /// <para>platformTag字段为文件名中含有的平台标识，根据自己的需求填写</para>
    /// </summary>
    /// <param name="apiUrl">Api链接，不要带/latest后缀</param>
    private static bool GetLatestVersionAndDownloadUrl(string apiUrl, string platformTag, out string? latestVersionString, out string? downloadUrl)
    {
        apiUrl += "/latest"; //获取最新版本，不论是否为pre-release
        latestVersionString = string.Empty;
        downloadUrl = string.Empty;
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("request");
        httpClient.DefaultRequestHeaders.Accept.TryParseAdd("application/json");

        try
        {
            using var response = httpClient.GetAsync(apiUrl).Result;
            if (response.IsSuccessStatusCode)
            {
                using var read = response.Content.ReadAsStringAsync();
                read.Wait();
                string jsonString = read.Result;
                JObject json = JObject.Parse(jsonString);
                if (json == null)
                {
                    Debug.WriteLine("获取的Json为空");
                    return false;
                }
                latestVersionString = json["tag_name"]?.ToString();

                if (json["assets"] is JArray assetsJsonArray && assetsJsonArray.Count > 0)
                {
                    foreach (var assetJsonObject in assetsJsonArray)
                    {
                        string? browserDownloadUrl = assetJsonObject["browser_download_url"]?.ToString();
                        if (!string.IsNullOrEmpty(browserDownloadUrl))
                        {
                            if (browserDownloadUrl.Contains(platformTag))
                            {
                                if (browserDownloadUrl.EndsWith(".zip") || browserDownloadUrl.EndsWith(".7z") || browserDownloadUrl.EndsWith(".rar"))
                                {
                                    downloadUrl = browserDownloadUrl;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                Debug.WriteLine($"网络请求出错: {response.StatusCode} - {response.ReasonPhrase}");
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine($"网络请求出错: {e.Message}");
        }
        finally
        {
            httpClient.Dispose();
        }
        return true;
    }

    /// <summary>去除版本号前的v或者V</summary>
    private static string RemoveFirstLetterV(string input)
    {
        if (!string.IsNullOrEmpty(input) && (input[0] == 'v' || input[0] == 'V'))
        {
            return input.Substring(1);
        }
        return input;
    }

    /// <summary>
    /// 通过api获取最新的版本号，该版本可以不是pre-release版本
    /// </summary>
    private static string GetLatestVersionString(string apiUrl, bool getPrerelease)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("request");
        httpClient.DefaultRequestHeaders.Accept.TryParseAdd("application/json");

        try
        {
            var response = httpClient.GetAsync(apiUrl).Result;
            if (response.IsSuccessStatusCode)
            {
                var read = response.Content.ReadAsStringAsync();
                read.Wait();
                string jsonString = read.Result;
                var jsonArray = JArray.Parse(jsonString);
                if (jsonArray.Count == 0)
                {
                    Debug.WriteLine("获取JsonArray为空");
                    return string.Empty;
                }
                foreach (var jsonObject in jsonArray)
                {
                    // 是否需要更新到Prerelease版本
                    if (!getPrerelease && (bool)jsonObject["prerelease"])
                    {
                        return string.Empty;
                    }
                    return jsonObject["tag_name"].ToString();
                }
            }
            else if (response.StatusCode == HttpStatusCode.Forbidden && response.ReasonPhrase.Contains("403"))
            {
                Debug.WriteLine("GitHub API速率限制已超出，请稍后再试");
                throw new Exception("GitHub API速率限制已超出，请稍后再试");
            }
            else
            {
                Debug.WriteLine($"请求GitHub时发生错误: {response.StatusCode} - {response.ReasonPhrase}");
                throw new Exception($"请求GitHub时发生错误: {response.StatusCode} - {response.ReasonPhrase}");
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine($"处理GitHub响应时发生错误: {e.Message}");
            throw new Exception($"处理GitHub响应时发生错误: {e.Message}");
        }
        finally
        {
            httpClient.Dispose();
        }
        return string.Empty;
    }

    /// <summary>
    /// 通过api获取指定版本的文件下载链接
    /// </summary>
    private string GetDownloadUrl(string apiUrl, string version, string platformTag)
    {
        var releaseUrl = apiUrl + $"/tags/{version}";
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("request");
        httpClient.DefaultRequestHeaders.Accept.TryParseAdd("application/json");
        try
        {
            var response = httpClient.GetAsync(releaseUrl).Result;
            if (response.IsSuccessStatusCode)
            {
                var read = response.Content.ReadAsStringAsync();
                read.Wait();
                var jsonString = read.Result;
                var jsonObject = JObject.Parse(jsonString);
                if (jsonObject["assets"] is JArray assetsJsonArray && assetsJsonArray.Count > 0)
                {
                    foreach (var assetJsonObject in assetsJsonArray)
                    {
                        string? browserDownloadUrl = assetJsonObject["browser_download_url"]?.ToString();
                        if (!string.IsNullOrEmpty(browserDownloadUrl))
                        {
                            if (browserDownloadUrl.Contains(platformTag))
                            {
                                if (browserDownloadUrl.EndsWith(".zip") || browserDownloadUrl.EndsWith(".7z") || browserDownloadUrl.EndsWith(".rar"))
                                {
                                    return browserDownloadUrl;
                                }
                            }
                        }
                    }
                }
            }
            else if (response.StatusCode == HttpStatusCode.Forbidden && response.ReasonPhrase.Contains("403"))
            {
                Debug.WriteLine("GitHub API速率限制已超出，请稍后再试。");
                throw new Exception("GitHub API速率限制已超出，请稍后再试。");
            }
            else
            {
                Debug.WriteLine($"请求GitHub时发生错误: {response.StatusCode} - {response.ReasonPhrase}");
                throw new Exception($"请求GitHub时发生错误: {response.StatusCode} - {response.ReasonPhrase}");
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine($"处理GitHub响应时发生错误: {e.Message}");
            throw new Exception($"处理GitHub响应时发生错误: {e.Message}");
        }
        finally
        {
            httpClient.Dispose();
        }

        return string.Empty;
    }

    /// <summary>
    /// 下载并解压文件，解压支持.zip .rar .7z，其他格式需要新增代码处理
    /// </summary>
    private async Task<bool> DownloadAndExtractFile(string url, string tempFileDirectory, string tempFileName)
    {
        string tempFilePath = Path.Combine(tempFileDirectory, tempFileName);
        Debug.WriteLine(tempFilePath);
        try
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            long? contentLength = response.Content.Headers.ContentLength;
            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write);
            byte[] buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead = 0;
            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                totalRead += bytesRead;

                if (contentLength.HasValue)
                {
                    double percentage = ((double)totalRead / contentLength.Value) * 100;
                    string totalReadMB = ((double)totalRead / 1024f / 1024f).ToString("0.00");
                    string contentLengthMB = ((double)contentLength / 1024f / 1024f).ToString("0.00");
                    mainWindow.downloadInfoText.Text = $"{totalReadMB}MB / {contentLengthMB}MB";
                    mainWindow.downloadProgressBar.Value = percentage;
                }
                // 保存文件
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
            }
            fileStream.Close();

            //解压文件
            var extractDir = Path.Combine(tempFileDirectory, Path.GetFileNameWithoutExtension(tempFileName));
            if (Directory.Exists(extractDir))
            {
                Directory.Delete(extractDir, true);
            }
            if (!File.Exists(tempFilePath))
            {
                Debug.WriteLine("找不到已下载的文件!");
                return false;
            }
            switch (Path.GetExtension(tempFilePath))
            {
                case ".zip":
                    ZipFile.ExtractToDirectory(tempFilePath, extractDir);
                    break;
                case ".rar":
                case ".7z":
                    Directory.CreateDirectory(extractDir);
                    var archive = ArchiveFactory.Open(tempFilePath);
                    foreach (var entry in archive.Entries)
                    {
                        if (!entry.IsDirectory)
                        {
                            if (!Directory.Exists(extractDir))
                            {
                                Directory.CreateDirectory(extractDir);
                            }
                            entry.WriteToDirectory(extractDir, new ExtractionOptions()
                            {
                                ExtractFullPath = true,
                                Overwrite = true
                            });
                        }
                    }
                    break;
            }
        }
        catch (HttpRequestException httpEx)
        {
            Debug.WriteLine($"HTTP请求出现异常: {httpEx.Message}");
            return false;
        }
        catch (IOException ioEx)
        {
            Debug.WriteLine($"文件操作出现异常: {ioEx.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"出现未知异常: {ex.Message}");
            return false;
        }
        return true;
    }
}