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
    LoadInitConfig();

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

      byte[] buffer = new byte[8192];
      int bytesRead = stream.Read(buffer, 0, buffer.Length);
      // Convierte los bytes leídos en texto
      string requestText = Encoding.UTF8.GetString(buffer, 0, bytesRead);

      // Separar encabezados y body
      string[] requestParts = requestText.Split("\r\n\r\n", 2, StringSplitOptions.None);
      string headers = requestParts[0];
      string body = requestParts.Length > 1 ? requestParts[1] : "";

      // Extraer método http y URL
      string[] headerLines = headers.Split("\r\n");
      string[] requestLineParts = headerLines[0].Split(' ');
      if (requestLineParts.Length < 2) return;

      string method = requestLineParts[0];
      string rawUrl = requestLineParts[1];

      // Ignorar peticiones automáticas del navegador
      if (rawUrl.ToLower().StartsWith("/.well-known") || rawUrl.ToLower().StartsWith("/favicon.ico")) return; 


      // Separar la URL y los parámetros de consulta
      string[] urlParts = rawUrl.Split('?');
      string url = urlParts[0];  // URL
      string queryParams = urlParts.Length > 1 ? urlParts[1] : null;  // Parámetros de consulta
      
      string code = "200";

      ClientRequest ClientData = new();

      if (method == "GET")
      {
        if (url == "/")
        {
          url = "/index.html";
        }

        url = Path.Combine(AppContext.BaseDirectory, Root, url.TrimStart('/'));

        if (url.EndsWith(".html") && File.Exists(url))
        {
          RenderPage(stream, url, code); // Se retornará un archivo html
        }
        else if (url.EndsWith(".txt") && File.Exists(url))
        {
          SendLog(stream, url); // Se retornará un archivo txt
        }
        else if (File.Exists(url))
        {
          SendStaticFile(stream, url); // Buscará otros archivos (png, js, css)
        }
        else
        {
          code = "404";
          RenderPage(stream, Path.Combine(AppContext.BaseDirectory, Root, "error.html"), code); // Se retornará el error.html
        }
      }
      else if (method == "POST")
      {
        // Obtener Content-Length para leer el body completo
        int contentLength = 0;
        foreach (string line in headerLines)
        {
          if (line.StartsWith("Content-Length:"))
          {
            contentLength = int.Parse(line.Split(":")[1].Trim());
            break;
          }
        }

        // Si el body es más grande que el buffer inicial, leer el resto
        if (body.Length < contentLength)
        {
          int remainingBytes = contentLength - body.Length;
          byte[] extraBuffer = new byte[remainingBytes];
          int extraBytesRead = stream.Read(extraBuffer, 0, remainingBytes);
          body += Encoding.UTF8.GetString(extraBuffer, 0, extraBytesRead);
        }

        ClientData.Body = body;

        RenderPage(stream, Path.Combine(AppContext.BaseDirectory, Root, "post.html"), code);
      }


      ClientData.Code = code;
      ClientData.Method = method;
      ClientData.UrlParams = queryParams;
      // ClientData.RequestedFile = url;
      ClientData.RequestedFile = Path.GetFileName(url);
      ClientData.Ip = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
      ClientData.Date = DateTime.Now;
      SaveLog(ClientData);
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

  static void SendStaticFile(NetworkStream stream, string path)
  {
    string contentType = "text/plain";

    if (path.EndsWith(".css")) contentType = "text/css";
    else if (path.EndsWith(".js")) contentType = "application/javascript";
    else if (path.EndsWith(".png")) contentType = "image/png";

    byte[] body = File.ReadAllBytes(path);
    string header = "HTTP/1.1 200 OK\r\n" +
                    $"Content-Type: {contentType}\r\n" +
                    $"Content-Length: {body.Length}\r\n" +
                    "\r\n";

    byte[] headerBytes = System.Text.Encoding.UTF8.GetBytes(header);
    stream.Write(headerBytes, 0, headerBytes.Length);
    stream.Write(body, 0, body.Length);
  }


static void RenderPage(NetworkStream stream, string page, string code)
{
    
    // string rootDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "pages");
    // string filePath = Path.Combine(rootDir, page);

    // string content = File.ReadAllText(filePath);
    string content = File.ReadAllText(page);
    string statusText = code == "200" ? "OK" : "Not Found";

    string header = 
        $"HTTP/1.1 {code} {statusText}\r\n" +
        "Content-Type: text/html; charset=UTF-8\r\n" +
        $"Content-Length: {Encoding.UTF8.GetByteCount(content)}\r\n" +
        "Connection: close\r\n" +
        "\r\n";

    byte[] headerBytes = Encoding.UTF8.GetBytes(header);
    byte[] contentBytes = Encoding.UTF8.GetBytes(content);

    stream.Write(headerBytes, 0, headerBytes.Length);
    stream.Write(contentBytes, 0, contentBytes.Length);
    stream.Flush();
}


  static void SendLog(NetworkStream stream, string filePath)
  {
    byte[] fileBytes = File.ReadAllBytes(filePath);

    using MemoryStream compressedStream = new();
    using (GZipStream gzip = new(compressedStream, CompressionMode.Compress, leaveOpen: true))
    {
      gzip.Write(fileBytes, 0, fileBytes.Length);
      gzip.Flush();
    }

    string fileName = Path.GetFileName(filePath) + ".gz";

    using (StreamWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
    {
      writer.WriteLine("HTTP/1.1 200 OK");
      writer.WriteLine("Content-Encoding: gzip");
      writer.WriteLine("Content-Type: application/octet-stream");
      writer.WriteLine($"Content-Disposition: attachment; filename=\"{fileName}\"");
      writer.WriteLine($"Content-Length: {compressedStream.Length}");
      writer.WriteLine();
      writer.Flush();
    }

    compressedStream.Position = 0;
    compressedStream.CopyTo(stream);
    stream.Flush();
  }


  static void SaveLog(ClientRequest ClientData)
  {
    string logsDir = Path.Combine(AppContext.BaseDirectory, Root);
    Directory.CreateDirectory(logsDir); 
    string logFile = Path.Combine(logsDir, $"log_{DateTime.UtcNow:yyyy-MM-dd}.txt");
    File.AppendAllText(logFile, $"{ClientData}\n");
  }

  static void LoadInitConfig()
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
    public string Code { get; set; } = "";
    public string Method { get; set; } = "";
    public string UrlParams { get; set; } = "";
    public string RequestedFile { get; set; } = "";
    public string Ip { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime Date { get; set; }

    public override string ToString()
    {
      return $"[{Date}] {Ip} {Method} {Code} {RequestedFile} {UrlParams} {Body.Replace("\r", "").Replace("\n", "")}";
    }
  }
}