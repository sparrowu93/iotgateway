using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FileParser.Models;
using FileParser.Interfaces;
using FluentFTP;
using SMBLibrary;
using SMBLibrary.Client;
using Newtonsoft.Json.Linq;
using FileAttributes = SMBLibrary.FileAttributes;

namespace FileParser.Services;

public class FileMonitor : IDisposable
{
    private readonly FileParserConfig _config;
    private readonly List<IFileParser> _parsers;
    private readonly DataCache _dataCache;
    private readonly Timer _timer;
    private bool _isRunning;

    public FileMonitor(FileParserConfig config, List<IFileParser> parsers, DataCache dataCache)
    {
        _config = config;
        _parsers = parsers;
        _dataCache = dataCache;
        _timer = new Timer(OnTimerCallback, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        _isRunning = true;
        _timer.Change(0, _config.ScanInterval);
    }

    public void Stop()
    {
        _isRunning = false;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private async void OnTimerCallback(object? state)
    {
        if (!_isRunning)
            return;

        try
        {
            var files = await GetFilesAsync();
            foreach (var file in files)
            {
                if (!ShouldProcessFile(file))
                    continue;

                var content = await ReadFileContentAsync(file);
                if (content == null)
                    continue;

                foreach (var parser in _parsers)
                {
                    if (parser.CanParse(file))
                    {
                        var data = await parser.ParseAsync(content);
                        _dataCache.UpdateData(file, data);
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"监控出错: {ex.Message}");
        }
    }

    private async Task<List<string>> GetFilesAsync()
    {
        var files = new List<string>();

        switch (_config.ServerType.ToUpper())
        {
            case "FTP":
                files = await GetFtpFilesAsync();
                break;
            case "SMB":
                files = await GetSmbFilesAsync();
                break;
            default:
                files = GetLocalFiles();
                break;
        }

        return files;
    }

    private async Task<List<string>> GetFtpFilesAsync()
    {
        var files = new List<string>();
        using var client = new AsyncFtpClient(_config.ServerAddress, _config.Username, _config.Password);
        
        try
        {
            await client.Connect();
            var listing = await client.GetListing(_config.MonitorPath);
            
            foreach (var item in listing)
            {
                if (item.Type == FtpObjectType.File && 
                    item.FullName.EndsWith(_config.FilePattern.TrimStart('*')))
                {
                    files.Add(item.FullName);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FTP获取文件列表失败: {ex.Message}");
        }

        return files;
    }

    private async Task<List<string>> GetSmbFilesAsync()
    {
        var files = new List<string>();
        var client = new SMB2Client();
        
        try
        {
            var connected = client.Connect(
                _config.ServerAddress, 
                SMBTransportType.DirectTCPTransport);

            if (!connected)
                return files;

            NTStatus loggedIn = client.Login(string.Empty, _config.Username, _config.Password);
            if (loggedIn != NTStatus.STATUS_SUCCESS)
                return files;

            NTStatus status;
            var shares = client.ListShares(out status);
            if (status != NTStatus.STATUS_SUCCESS || shares == null)
                return files;

            foreach (var share in shares)
            {
                var fileStore = client.TreeConnect(share, out status);
                if (status != NTStatus.STATUS_SUCCESS)
                    continue;

                object directoryHandle;
                FileStatus fileStatus;
                status = fileStore.CreateFile(
                    out directoryHandle,
                    out fileStatus,
                    string.Empty,
                    AccessMask.GENERIC_READ,
                    FileAttributes.Directory,
                    ShareAccess.Read | ShareAccess.Write,
                    CreateDisposition.FILE_OPEN,
                    CreateOptions.FILE_DIRECTORY_FILE,
                    null);

                if (status == NTStatus.STATUS_SUCCESS)
                {
                    List<QueryDirectoryFileInformation> fileList;
                    status = fileStore.QueryDirectory(
                        out fileList,
                        directoryHandle,
                        _config.FilePattern,
                        FileInformationClass.FileDirectoryInformation);

                    if (status == NTStatus.STATUS_SUCCESS && fileList != null)
                    {
                        foreach (var file in fileList)
                        {
                            if (file is FileDirectoryInformation fileInfo)
                            {
                                files.Add(Path.Combine(_config.MonitorPath, fileInfo.FileName));
                            }
                        }
                    }

                    fileStore.CloseFile(directoryHandle);
                }

                fileStore.Disconnect();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SMB获取文件列表失败: {ex.Message}");
        }

        return files;
    }

    private List<string> GetLocalFiles()
    {
        try
        {
            return Directory.GetFiles(
                _config.MonitorPath, 
                _config.FilePattern, 
                SearchOption.TopDirectoryOnly).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"本地获取文件列表失败: {ex.Message}");
            return new List<string>();
        }
    }

    private bool ShouldProcessFile(string filePath)
    {
        if (string.IsNullOrEmpty(_config.FilePrefix))
            return true;

        var fileName = Path.GetFileName(filePath);
        return fileName.StartsWith(_config.FilePrefix);
    }

    private async Task<string?> ReadFileContentAsync(string filePath)
    {
        try
        {
            switch (_config.ServerType.ToUpper())
            {
                case "FTP":
                    return await ReadFtpFileAsync(filePath);
                case "SMB":
                    return await ReadSmbFileAsync(filePath);
                default:
                    return await File.ReadAllTextAsync(filePath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"读取文件失败: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> ReadFtpFileAsync(string filePath)
    {
        using var client = new AsyncFtpClient(_config.ServerAddress, _config.Username, _config.Password);
        using var memoryStream = new MemoryStream();
        
        try
        {
            await client.Connect();
            await client.DownloadStream(memoryStream, filePath);
            memoryStream.Position = 0;
            using var reader = new StreamReader(memoryStream);
            return await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FTP读取文件失败: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> ReadSmbFileAsync(string filePath)
    {
        var client = new SMB2Client();
        
        try
        {
            var connected = client.Connect(
                _config.ServerAddress, 
                SMBTransportType.DirectTCPTransport);

            if (!connected)
                return null;

            NTStatus loggedIn = client.Login(string.Empty, _config.Username, _config.Password);
            if (loggedIn != NTStatus.STATUS_SUCCESS)
                return null;

            NTStatus status;
            var shares = client.ListShares(out status);
            if (status != NTStatus.STATUS_SUCCESS || shares == null)
                return null;

            foreach (var share in shares)
            {
                var fileStore = client.TreeConnect(share, out status);
                if (status != NTStatus.STATUS_SUCCESS)
                    continue;

                object fileHandle;
                FileStatus fileStatus;
                status = fileStore.CreateFile(
                    out fileHandle,
                    out fileStatus,
                    filePath,
                    AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE,
                    FileAttributes.Normal,
                    ShareAccess.Read,
                    CreateDisposition.FILE_OPEN,
                    CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
                    null);

                if (status == NTStatus.STATUS_SUCCESS)
                {
                    using var memoryStream = new MemoryStream();
                    byte[] data;
                    long bytesRead = 0;

                    while (true)
                    {
                        status = fileStore.ReadFile(out data, fileHandle, bytesRead, 4096);
                        if (status != NTStatus.STATUS_SUCCESS && status != NTStatus.STATUS_END_OF_FILE)
                        {
                            break;
                        }

                        if (status == NTStatus.STATUS_END_OF_FILE || data.Length == 0)
                        {
                            break;
                        }

                        memoryStream.Write(data, 0, data.Length);
                        bytesRead += data.Length;
                    }

                    fileStore.CloseFile(fileHandle);
                    fileStore.Disconnect();

                    memoryStream.Position = 0;
                    using var reader = new StreamReader(memoryStream);
                    return await reader.ReadToEndAsync();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SMB读取文件失败: {ex.Message}");
        }

        return null;
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
