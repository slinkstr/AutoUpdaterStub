using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO.Compression;

internal class Program
{
    private static readonly HttpClient httpClient = new();

    private static async Task Main(string[] args)
    {
        try
        {
            if(StubConfig.manifestUrl == null || StubConfig.programExeName == null)
            {
                throw new Exception("Invalid URL or program name - must be provided at compile time.");
            }

            string installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StubConfig.programExeName);
            string tempDir = Path.Combine(Path.GetTempPath(), StubConfig.programExeName + "Temp");
            string exePath = Path.Combine(installDir, StubConfig.programExeName + ".exe");

            Version? currentVersion = null;
            if (File.Exists(exePath))
            {
                var currentVersionInfo = FileVersionInfo.GetVersionInfo(exePath);
                if(!string.IsNullOrWhiteSpace(currentVersionInfo.FileVersion))
                {
                    currentVersion = Version.Parse(currentVersionInfo.FileVersion);
                }
            }
            Console.WriteLine($"Current version: {(currentVersion == null ? "not installed" : currentVersion)}");
            Console.WriteLine("Checking for updates...");
            try
            {
                var response = await httpClient.GetAsync(StubConfig.manifestUrl);
                var content = await response.Content.ReadAsStringAsync();
                response.EnsureSuccessStatusCode();
                var jsonObject = JsonNode.Parse(content) as JsonObject ?? throw new ArgumentNullException("Unable to parse JSON from server. Content: " + content);
                string? remoteVersionString = (string?)jsonObject["version"];
                string? remoteDownloadString = (string?)jsonObject["download"];
                if (string.IsNullOrWhiteSpace(remoteVersionString) || string.IsNullOrWhiteSpace(remoteDownloadString))
                {
                    throw new Exception("Error reading remote manifest. Server response: " + content);
                }

                var remoteVersion = Version.Parse(remoteVersionString);

                if (currentVersion < remoteVersion)
                {
                    Console.WriteLine($"Downloading new version... ({remoteVersion})");
                    await InstallProgram(remoteDownloadString, tempDir, installDir);
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Up to date!");
                    Console.ResetColor();
                }
            }
            catch (Exception exc)
            {
                string errorMessage = $"Error checking for updates: {exc.Message}";
                if(currentVersion == null)
                {
                    throw new Exception(errorMessage);
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(errorMessage);
                    Console.ResetColor();
                    await Task.Delay(5000);
                }
            }

            await Task.Delay(1000);
            OpenProgram(exePath, args);
        }
        catch (Exception exc)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"{exc.Message}");
            Console.ResetColor();
            Console.WriteLine("Press enter to continue...");
            Console.ReadLine();
        }
    }

    private static async Task InstallProgram(string url, string tempFolder, string destinationFolder)
    {
        string filename = url.Split("/").Last();
        var tempFile = Path.Combine(tempFolder, filename);
        if (Directory.Exists(tempFolder))        { Directory.Delete(tempFolder, true); }
        try
        {
            Directory.CreateDirectory(tempFolder);
            Directory.CreateDirectory(destinationFolder);

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? resultUri))
            {
                throw new Exception($"Unable to parse URL \"{url}\"");
            }

            var response = await httpClient.GetAsync(resultUri);
            using (var filestream = new FileStream(tempFile, FileMode.Create))
            {
                await response.Content.CopyToAsync(filestream);
            }
            using (ZipArchive source = ZipFile.Open(tempFile, ZipArchiveMode.Read, null))
            {
                foreach (ZipArchiveEntry entry in source.Entries)
                {
                    string fullPath = Path.Combine(destinationFolder, entry.FullName);
                    if (Path.GetFileName(fullPath).Length != 0)
                    {
                        var directory = Path.GetDirectoryName(fullPath);
                        if(directory == null) { throw new ArgumentNullException("Unable to extract archive, directory name was null."); }
                        Directory.CreateDirectory(directory);
                        entry.ExtractToFile(fullPath, true);
                    }
                }
            }
        }
        finally
        {
            if (Directory.Exists(tempFolder)) { Directory.Delete(tempFolder, true); }
        }
    }

    private static void OpenProgram(string filepath, string[] args)
    {
        if (!File.Exists(filepath))
        {
            throw new Exception($"Could not find {filepath}");
        }

        ProcessStartInfo procInfo = new ProcessStartInfo()
        {
            UseShellExecute = StubConfig.useShellExecute,
            FileName = filepath,
            WorkingDirectory = Path.GetDirectoryName(filepath),
            Verb = StubConfig.runElevated ? "runas" : "",
            Arguments = string.Join(" ", args)
        };
        Process.Start(procInfo);
    }
}