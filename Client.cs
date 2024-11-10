using System.Net.Sockets;
using System.Text;
using System.Text.Json;

class Program
{
    static async Task Main()
    {

        while (true)
        {
            Console.WriteLine("Choose an action:\n1. Download\n2. Search by tags\n3. Status\n4. Rename\n5. Move\n6. Delete\n7. Exit");
            string choice = Console.ReadLine()!;

            if (choice == "7") break;

            ClientRequest request = choice switch
            {
                "1" => await CreateDownloadRequest(),
                "2" => CreateRequest("search", tags: await Prompt("Enter search tags: ")),
                "3" => CreateRequest("status"),
                "4" => CreateRequest("rename", name: await Prompt("Enter current file name: "), newName: await Prompt("Enter new file name: ")),
                "5" => CreateRequest("move", name: await Prompt("Enter file name to move: "), savePath: await Prompt("Enter new directory path: ")),
                "6" => CreateRequest("delete", name: await Prompt("Enter file name to delete: ")),
                _ => null
            };

            if (request != null)
            {
                await SendRequestAsync(request);
            }
            else
            {
                Console.WriteLine("Invalid choice. Please try again.");
            }
        }
    }

    static async Task<string> Prompt(string message)
    {
        Console.WriteLine(message);
        return Console.ReadLine()!;
    }

    static async Task<ClientRequest> CreateDownloadRequest()
    {
        return new ClientRequest
        {
            Action = "download",
            Url = await Prompt("Enter web URL: "),
            SavePath = await Prompt("Enter save path: "),
            Name = await Prompt("Enter file name: "),
            Extension = await Prompt("Enter file extension (e.g., .jpg, .png): "),
            ThreadCount = int.TryParse(await Prompt("Enter number of threads: "), out int threads) ? threads : 1,
            Tags = await Prompt("Enter tags (comma-separated, optional): ") ?? string.Empty
        };
    }

    static ClientRequest CreateRequest(string action, string name = null, string url = null, string savePath = null, string newName = null, string tags = null)
    {
        return new ClientRequest
        {
            Action = action,
            Name = name,
            Url = url,
            SavePath = savePath,
            NewName = newName,
            Tags = tags
        };
    }

    static async Task SendRequestAsync(ClientRequest request)
    {
        try
        {
            using TcpClient client = new TcpClient("127.0.0.1", 6666);
            using NetworkStream stream = client.GetStream();
            using StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
            using StreamReader reader = new StreamReader(stream, Encoding.UTF8);

            string jsonRequest = JsonSerializer.Serialize(request);
            await writer.WriteLineAsync(jsonRequest);

            string response = await reader.ReadLineAsync();
            if (response != null)
            {
                Console.WriteLine($"Response from server: {response}");
            }
            else
            {
                Console.WriteLine("No response from server. Server may have closed the connection.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error connecting to server: {ex.Message}");
        }
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