using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;


internal class Program
{
  private static string Root;
  private static int Port;
  static void Main(string[] args)
  {
    LoadConfig();

    TcpListener server = new(IPAddress.Any, Port);
    server.Start();
    Console.WriteLine($"Servidor iniciado en http://localhost:{Port}/");

    while (true)
    {
      try
      {
        TcpClient client = server.AcceptTcpClient();
        Thread clientThread = new(() => HandleClient(client));
        clientThread.Start();
      }
      catch (Exception ex)
      {
        Console.WriteLine($"Error al aceptar cliente: {ex.Message}");
      }
    }

  }


  static void HandleClient(TcpClient client)
  {
    try
    {
      using NetworkStream stream = client.GetStream();
      using StreamReader reader = new(stream, Encoding.UTF8);
      using StreamWriter writer = new(stream, Encoding.UTF8) { AutoFlush = true };

      string requestLine = reader.ReadLine();
      if (string.IsNullOrEmpty(requestLine)) return;

      Console.WriteLine($"Solicitud recibida: {requestLine}");

      ClientRequest ClientData = new();

      string[] requestParts = requestLine.Split(' ');
      if (requestParts.Length < 2) return;

      string method = requestParts[0];
      string rawUrl = requestParts[1];

      // Separar la URL y los parámetros de consulta
      string[] urlParts = rawUrl.Split('?');
      string url = urlParts[0];  // URL
      string queryParams = urlParts.Length > 1 ? urlParts[1] : null;  // Parámetros de consulta
      string code = "200";
      if (method == "GET")
      {
        // Se construye la ruta del archivo log o html
        url = FilePathBuild(url);

        if (url == "/index.html")
        {
          Index(writer); // Se retornará el index.html
        }
        else if (File.Exists(url))
        {
          SendResponse(writer, url); // Se retornará un archivo log
        }
        else
        {
          Error(writer); // Se retornará el error.html
          code = "404";
        }
      }
      else if (method == "POST")
      {
        ClientData.Body = reader.ReadToEnd();
        writer.WriteLine("HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\n\r\nDatos recibidos");
      }

      ClientData.Code = code;
      ClientData.Method = method;
      ClientData.UrlParams = queryParams;
      ClientData.RequestedFile = url;
      ClientData.Ip = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
      ClientData.Date = DateTime.Now;
      LogRequest(client, ClientData);
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Error en el cliente: {ex.Message}");
    }
    finally
    {
      client.Close();
    }      
  }

  static string FilePathBuild(string url)
  {
    if (url == "/")
    {
      return "/index.html";
    }
    else
    {
      string logPath = Path.Combine(AppContext.BaseDirectory, Root, url.TrimStart('/'));
      if (!Path.HasExtension(logPath))
      {
        logPath += ".txt";
      }

      // Console.WriteLine($"Buscando archivo en: {logPath}");
      return logPath;
    }
  }
  static void Index(StreamWriter writer)
  {
    string rootDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "pages");
    string filePath = Path.Combine(rootDir, "index.html");
    string content = File.ReadAllText(filePath);

    writer.WriteLine("HTTP/1.1 200 OK");
    writer.WriteLine($"Content-Length: {content.Length}");
    writer.WriteLine("Content-Type: text/html\r\n");
    writer.WriteLine();
    writer.Write(content);
    writer.Flush();
  }

  static void Error(StreamWriter writer)
  {
    string rootDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "pages");
    string filePath = Path.Combine(rootDir, "error.html");
    string content = File.ReadAllText(filePath);

    writer.WriteLine("HTTP/1.1 404 OK");
    writer.WriteLine($"Content-Length: {content.Length}");
    writer.WriteLine("Content-Type: text/html\r\n");
    writer.WriteLine(); 
    writer.Write(content);
    writer.Flush();
  }
  static void SendResponse(StreamWriter writer, string filePath)
  {
    byte[] fileBytes = File.ReadAllBytes(filePath);

    using MemoryStream compressedStream = new();
    using (GZipStream gzip = new(compressedStream, CompressionMode.Compress, leaveOpen: true))
    {
      gzip.Write(fileBytes, 0, fileBytes.Length);
      gzip.Flush();
    }

    writer.WriteLine("HTTP/1.1 200 OK");
    writer.WriteLine("Content-Encoding: gzip");
    writer.WriteLine($"Content-Length: {compressedStream.Length}");
    writer.WriteLine("Content-Type: text/html\r\n");

    writer.Flush();

    compressedStream.Position = 0;
    compressedStream.CopyTo(writer.BaseStream);
  }


  static void LogRequest(TcpClient client, ClientRequest ClientData)
  {
    string logsDir = Path.Combine(AppContext.BaseDirectory, Root);
    Directory.CreateDirectory(logsDir); 
    string logFile = Path.Combine(logsDir, $"log_{DateTime.UtcNow:yyyy-MM-dd}.txt");
    File.AppendAllText(logFile, $"{ClientData}\n");
  }

  static void LoadConfig()
  {
    string configText = File.ReadAllText("config.json");
    var config = JsonSerializer.Deserialize<Config>(configText);

    Root = config.Root;
    Port = config.Port;
  }

class Config
{
  public int Port { get; set; }
  public string Root { get; set; }
}

  class ClientRequest
  {
    public string? Ip { get; set; }
    public string? Method { get; set; }
    public string? Code { get; set; }
    public DateTime? Date { get; set; }
    public string? RequestedFile { get; set; }
    public string? UrlParams { get; set; }
    public string? Body { get; set; }
  
    public override string ToString()
    {
        return $"[{Date}] {Ip} {Method} {Code} {RequestedFile} {UrlParams} {Body}";
    }
  }
}