using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

public enum DownloadStatus
{
    Ongoing,
    Successful,
    Failed
}

class Server
{
    static ConcurrentDictionary<string, DownloadTask> downloadTasks = new ConcurrentDictionary<string, DownloadTask>();
    static string connectionString = @"Server=(localdb)\MSSQLLocalDB;Database=ImageDownloadsDB;";

    static async Task Main()
    {
        await Updates.LoadDownloadTasksFromDatabase(downloadTasks);

        TcpListener listener = new TcpListener(IPAddress.Any, 6666);
        listener.Start();
        Console.WriteLine("Server started on port 6666.");

        while (true)
        {
            TcpClient client = await listener.AcceptTcpClientAsync();
            Task.Run(() => HandleClient(client));
            //Console.WriteLine("Client connected.");
        }
    }

    static async Task HandleClient(TcpClient client)
    {
        using NetworkStream stream = client.GetStream();
        using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
        using StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

        try
        {
            string jsonRequest;
            while ((jsonRequest = await reader.ReadLineAsync()) != null)
            {
                Console.WriteLine($"Received request: {jsonRequest}");

                var request = JsonSerializer.Deserialize<ClientRequest>(jsonRequest);
                if (request == null || string.IsNullOrEmpty(request.Action))
                {
                    await writer.WriteLineAsync("Invalid request format.");
                    continue;
                }

                string response = request.Action.ToLower() switch
                {
                    "download" => HandleDownload(request),
                    "search" => HandleSearch(request.Tags),
                    "status" => HandleStatus(),
                    "rename" => HandleRename(request.Name, request.NewName),
                    "move" => HandleMove(request.Name, request.SavePath),
                    "delete" => HandleDelete(request.Name),
                    _ => "Invalid action."
                };

                Console.WriteLine($"Action '{request.Action}' processed. Sending response to client.");
                await writer.WriteLineAsync(response);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling client: {ex.Message}");
        }
    }

    static string HandleDownload(ClientRequest request)
    {
        if (!downloadTasks.ContainsKey(request.Name))
        {
            string fullPath = Path.Combine(request.SavePath, $"{request.Name}{request.Extension}");

            Directory.CreateDirectory(request.SavePath);

            var downloadTask = new DownloadTask(request.Name, request.Url, fullPath, request.Tags, request.ThreadCount);
            downloadTask.StatusChanged += (name, status) => Updates.UpdateDownloadStatusInDb(name, status);

            if (downloadTasks.TryAdd(request.Name, downloadTask))
            {
                downloadTask.Start();
                Updates.SaveMetadataToDatabase(request, fullPath);
                return "Download started and database updated.";
            }
        }
        return "Download with this name already exists.";
    }


    static string HandleSearch(string tags)
    {
        using SqlConnection connection = new SqlConnection(connectionString);
        connection.Open();

        string query = string.IsNullOrEmpty(tags)
            ? "SELECT * FROM Downloads"
            : "SELECT * FROM Downloads WHERE Tags LIKE @Tags";

        using SqlCommand command = new SqlCommand(query, connection);
        if (!string.IsNullOrEmpty(tags))
        {
            command.Parameters.AddWithValue("@Tags", $"%{tags}%");
        }

        using SqlDataReader reader = command.ExecuteReader();
        var results = new StringBuilder();
        while (reader.Read())
        {
            results.AppendLine($"ID: {reader["Id"]}, URL: {reader["Url"]}, FilePath: {reader["FilePath"]}, Name: {reader["Name"]}, " +
                $"Extension: {reader["Extension"]}, Tags: {reader["Tags"]}, Threads: {reader["ThreadCount"]}");
        }
        return results.Length > 0 ? results.ToString() : "No results found.";
    }



    static string HandleStatus()
    {
        var statusReport = new StringBuilder();
        foreach (var task in downloadTasks)
        {
            statusReport.AppendLine($"{task.Key}: {task.Value.Status}");
        }
        return statusReport.Length > 0 ? statusReport.ToString() : "No downloads.";
    }

    static string HandleRename(string name, string newName)
    {
        try
        {
            if (downloadTasks.TryGetValue(name, out var task))
            {
                task.Rename(newName);

                string newFilePath = Path.Combine(Path.GetDirectoryName(task.FilePath), newName + Path.GetExtension(task.FilePath));

                downloadTasks.TryRemove(name, out _);
                downloadTasks.TryAdd(newName, task);

                Updates.UpdateDownloadNameInDatabase(name, newName);
                Updates.UpdateDownloadFilePathInDb(name, newFilePath);

                return "File renamed and database updated.";
            }
            return "File not found.";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error renaming file: {ex.Message}");
            return "File rename failed on server.";
        }
    }


    static string HandleMove(string name, string newFilePath)
    {
        if (downloadTasks.TryGetValue(name, out var task))
        {
            try
            {
                string updatedFilePath = Path.Combine(newFilePath, Path.GetFileName(task.FilePath));
                if (File.Exists(updatedFilePath))
                {
                    File.Delete(updatedFilePath);
                }
                Console.WriteLine(updatedFilePath);
                task.Move(newFilePath);
                Updates.UpdateDownloadFilePathInDb(name, updatedFilePath);
                return $"File moved and database updated.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error moving file: {ex.Message}");

                return "File move failed on server.";
            }

        }
        return "File not found.";

    }

    static string HandleDelete(string name)
    {
        if (downloadTasks.TryRemove(name, out var task))
        {
            try
            {
                Console.WriteLine($"[HandleDelete] Checking existence of file at path: {task.FilePath}");

                if (File.Exists(task.FilePath))
                {
                    task.Delete();
                    Console.WriteLine("[HandleDelete] File deleted from local filesystem.");
                }
                else
                {
                    Console.WriteLine($"[HandleDelete] File {task.FilePath} not found on disk. Removing from database only.");
                }

                Updates.DeleteDownloadFromDatabase(name);
                return "File deleted from server and database updated.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting file: {ex.Message}");
                return "File delete failed on server.";
            }
        }
        return "File not found.";
    }


}


public class ClientRequest
{
    public string Action { get; set; }
    public string Url { get; set; }
    public string SavePath { get; set; }
    public string Name { get; set; }
    public string Extension { get; set; }
    public int ThreadCount { get; set; }
    public string Tags { get; set; }
    public string NewName { get; set; }
}

public class DownloadTask
{
    public DownloadStatus Status { get; private set; }
    public event Action<string, DownloadStatus> StatusChanged;

    private string name;
    private string url;
    private string filePath;
    private string tags;
    private int threadCount;
    private CancellationTokenSource cts;
    private Thread downloadThread;
    public string FilePath => filePath;

    public DownloadTask(string name, string url, string filePath, string tags, int threadCount)
    {
        this.name = name;
        this.url = url;
        this.filePath = filePath;
        this.tags = tags;
        this.threadCount = threadCount;
        this.Status = DownloadStatus.Ongoing;
        cts = new CancellationTokenSource();

        Console.WriteLine($"[DownloadTask] Initialized with filePath: {filePath}");
    }

    public void Start()
    {
        downloadThread = new Thread(async () => await DownloadFile());
        downloadThread.Start();
    }

    private async Task DownloadFile()
    {
        using HttpClient client = new HttpClient();
        try
        {
            byte[] fileBytes = await client.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(filePath, fileBytes);
            UpdateStatus(DownloadStatus.Successful);
            Console.WriteLine($"Downloaded file to {filePath}");
        }
        catch (Exception ex)
        {
            UpdateStatus(DownloadStatus.Failed);
            Console.WriteLine($"Failed to download {url}: {ex.Message}");
        }
    }

    private void UpdateStatus(DownloadStatus newStatus)
    {
        Status = newStatus;
        StatusChanged?.Invoke(name, Status);
    }

    public void Rename(string newName)
    {
        string newFilePath = Path.Combine(Path.GetDirectoryName(filePath), newName + Path.GetExtension(filePath));
        File.Move(filePath, newFilePath);
        filePath = newFilePath;
        name = newName;
    }

    public void Move(string newDirectory)
    {
        string newFilePath = Path.Combine(newDirectory, Path.GetFileName(filePath));
        File.Move(filePath, newFilePath);
        filePath = newFilePath;
    }

    public void Delete()
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}