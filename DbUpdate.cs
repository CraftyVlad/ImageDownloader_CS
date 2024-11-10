using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;

public class Updates
{
    static string connectionString = @"Server=(localdb)\MSSQLLocalDB;Database=ImageDownloadsDB;";

    public static void SaveMetadataToDatabase(ClientRequest request, string filePath)
    {
        using SqlConnection connection = new SqlConnection(connectionString);
        connection.Open();

        string query = string.IsNullOrEmpty(request.Tags)
            ? "INSERT INTO Downloads (Url, FilePath, Name, Extension, ThreadCount, Status) VALUES (@Url, @FilePath, @Name, @Extension, @ThreadCount, @Status)"
            : "INSERT INTO Downloads (Url, FilePath, Name, Extension, Tags, ThreadCount, Status) VALUES (@Url, @FilePath, @Name, @Extension, @Tags, @ThreadCount, @Status)";

        using SqlCommand command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@Url", request.Url);
        command.Parameters.AddWithValue("@FilePath", filePath);
        command.Parameters.AddWithValue("@Name", request.Name);
        command.Parameters.AddWithValue("@Extension", request.Extension);
        command.Parameters.AddWithValue("@ThreadCount", request.ThreadCount);
        command.Parameters.AddWithValue("@Status", DownloadStatus.Ongoing.ToString());

        if (!string.IsNullOrEmpty(request.Tags))
        {
            command.Parameters.AddWithValue("@Tags", request.Tags);
        }

        command.ExecuteNonQuery();
    }



    public static async Task LoadDownloadTasksFromDatabase(ConcurrentDictionary<string, DownloadTask> downloadTasks)
    {
        using SqlConnection connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        string query = "SELECT * FROM Downloads";
        using SqlCommand command = new SqlCommand(query, connection);
        using SqlDataReader reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var name = reader["Name"].ToString();
            var url = reader["Url"].ToString();
            var filePath = reader["FilePath"].ToString();
            var tags = reader["Tags"].ToString();
            var threadCount = (int)reader["ThreadCount"];
            var status = Enum.Parse<DownloadStatus>(reader["Status"].ToString());

            var downloadTask = new DownloadTask(name, url, filePath, tags, threadCount);

            downloadTask.StatusChanged += (name, status) => UpdateDownloadStatusInDb(name, status);

            downloadTasks.TryAdd(name, downloadTask);
        }

        Console.WriteLine("Loaded download tasks from database.");
    }


    public static void UpdateDownloadStatusInDb(string name, DownloadStatus status)
    {
        using SqlConnection connection = new SqlConnection(connectionString);
        connection.Open();

        string query = "UPDATE Downloads SET Status = @Status WHERE Name = @Name";
        using SqlCommand command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@Status", status.ToString());
        command.Parameters.AddWithValue("@Name", name);

        command.ExecuteNonQuery();
        Console.WriteLine($"Status for file: '{name}' updated to {status}.");
    }

    public static void UpdateDownloadFilePathInDb(string name, string newFilePath)
    {
        using SqlConnection connection = new SqlConnection(connectionString);
        connection.Open();

        string query = "UPDATE Downloads SET FilePath = @FilePath WHERE Name = @Name";
        using SqlCommand command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@FilePath", newFilePath);
        command.Parameters.AddWithValue("@Name", name);

        command.ExecuteNonQuery();
        Console.WriteLine($"Path for file: '{name}' updated to '{newFilePath}'.");
    }

    public static void UpdateDownloadNameInDatabase(string oldName, string newName)
    {
        using SqlConnection connection = new SqlConnection(connectionString);
        connection.Open();

        string query = "UPDATE Downloads SET Name = @NewName WHERE Name = @OldName";
        using SqlCommand command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@NewName", newName);
        command.Parameters.AddWithValue("@OldName", oldName);

        command.ExecuteNonQuery();
        Console.WriteLine($"'{oldName}' updated to '{newName}'.");
    }

    public static void DeleteDownloadFromDatabase(string name)
    {
        using SqlConnection connection = new SqlConnection(connectionString);
        connection.Open();

        string query = "DELETE FROM Downloads WHERE Name = @Name";
        using SqlCommand command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@Name", name);

        command.ExecuteNonQuery();
        Console.WriteLine($"'{name}' deleted.");
    }
}