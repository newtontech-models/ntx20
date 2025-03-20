using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
using ntx20.api;
using ntx20.api.io;
using ntx20.api.utils;
using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ntx20.command.app
{
    class Command :ICommand
    {

        private static readonly ILogger _logger = Logging.LoggerFactory.CreateLogger("ntx20.command.app");
        internal static void Configure(CommandLineApplication command, CommandLineOptions options)
        {

            command.Description = $"core app";
            
            command.OnExecute(() =>
            {
                options.Command = new Command(command)
                {
                    Args = command.RemainingArguments.ToArray()
                };
                return 0;
            });

           
        }
        private readonly CommandLineApplication _app;
        private string[] Args { get; set; } 
        public Command(CommandLineApplication app)
        {
            _app = app;
        }
        private static readonly Google.Protobuf.JsonParser jsonParser = new Google.Protobuf.JsonParser(Google.Protobuf.JsonParser.Settings.Default.WithIgnoreUnknownFields(true));
        private async Task<api.proto.App> GetAppDef(string version, string app_home_remote, string app_home_local)
        {
            var remotefile = $"{app_home_remote}/apps/ntx20/versions/{version}.json";
            var t = await api.io.LazyStream.Input(remotefile).GetTextAsync();
            var app = jsonParser.Parse<api.proto.App>((t));
            return app;
        }   
        public async Task<int> RunAsync(CancellationToken breaker)
        {

            try
            {
                var pos = Args.Contains("run") ? Array.IndexOf(Args, "run") : Array.IndexOf(Args, "serve");
                if (pos > -1 && Args.Length > pos + 1)
                {
                    var resource = await LazyStream.Input(Args[pos + 1]).GetTextAsync();
                    var parts = resource.Split(":", 3);
                    if (Environment.GetEnvironmentVariable("NTX20_APP_PATH") == null)
                    {
                        Environment.SetEnvironmentVariable("NTX20_VERSION", parts[1]);
                    }
                }
            }
            catch
            {

            }

            var app_home_local = Environment.GetEnvironmentVariable("NTX20_HOME_LOCAL");
            if(app_home_local == null)
            {
                app_home_local  = Path.GetDirectoryName(typeof(Program).GetTypeInfo().Assembly.Location);
                Environment.SetEnvironmentVariable("NTX20_HOME_LOCAL", app_home_local);
            }



            if (Environment.GetEnvironmentVariable("NTX20_CACHEDIR") == null)
                Environment.SetEnvironmentVariable("NTX20_CACHEDIR", Path.Combine(app_home_local, "blobs"));

            var latest = Path.Combine(app_home_local, ".latest");
            var ntx20_version = Environment.GetEnvironmentVariable("NTX20_VERSION")?.Trim();

            var idx = Array.IndexOf(Args, "version");
            bool need_save = !File.Exists(latest);
            if ((Args.Length > idx + 2 && Args[idx + 1] == "set"))
            {
                if (Args[idx + 2] == "-h" || Args[idx + 2] == "--help")
                {

                }
                else
                {
                    ntx20_version = Args[idx + 2];
                    need_save = true;
                }
            }


            if (!File.Exists(latest) && ntx20_version == null)
            {
                ntx20_version = "latest";
            }

            var app_home_remote = Environment.GetEnvironmentVariable("NTX20_HOME_REMOTE");
            
            if (ntx20_version != null)
            {
                if (!File.Exists(Path.Combine(app_home_local, "apps", "ntx20", ntx20_version, ".core")))
                {
                    ntx20_version = (await GetAppDef(ntx20_version, app_home_remote, app_home_local)).Version;
                }
            }
            if (need_save || ntx20_version == null)
            {
                using var mutex = new Mutex(false, "ntx20-fetch-latest");
                var mutexAcquired = false;
                try
                {
                    mutexAcquired = mutex.WaitOne(60000);
                }
                catch (AbandonedMutexException)
                {
                    mutexAcquired = true;
                }

                if (!mutexAcquired)
                {
                    throw new Exception($"Some instance is blocking update to {ntx20_version}");
                }

                try
                {
                    if (need_save)
                    {
                        File.WriteAllText(latest, ntx20_version);
                    }
                    if (ntx20_version == null)
                    {
                        ntx20_version = File.ReadAllText(latest);
                    }

                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }


            if (!File.Exists(Path.Combine(app_home_local, "apps", "ntx20", ntx20_version, ".core")))
            {

                using var mutex = new Mutex(false, "ntx20-" + ntx20_version);
                var mutexAcquired = false;
                try
                {
                    mutexAcquired = mutex.WaitOne(60000);
                }
                catch (AbandonedMutexException)
                {
                    mutexAcquired = true;
                }

                if (!mutexAcquired)
                {
                    throw new Exception($"Some instance is blocking update to {ntx20_version}");
                }

                try
                {
                    var t = GetVersion(ntx20_version, app_home_remote, app_home_local, true);
                    t.Wait();
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }

            if (Args.Contains("serve"))
            {
                return Run(app_home_local, ntx20_version, Args);
            }
            else
            {
                return Run(app_home_local, ntx20_version, Args);
            }
        }
        async Task<string> GetVersion(string version, string app_home_remote, string app_home_local, bool withCore20)
        {
            if (File.Exists(Path.Combine(app_home_local, "apps", "ntx20", version, withCore20 ? ".core" : ".client")))
                return version;

            var app = await GetAppDef(version, app_home_remote, app_home_local);

            if (File.Exists(Path.Combine(app_home_local, "apps", "ntx20", app.Version, withCore20 ? ".core" : ".client")))
                return app.Version;

            _logger.LogInformation($"Fetching version {app.Version}");

            Directory.CreateDirectory(Path.Combine(Environment.GetEnvironmentVariable("NTX20_CACHEDIR")));
            foreach (var b in app.Blobs)
            {
                if (withCore20 || b.Platform == null || b.Platform.Trim().Length == 0)
                {
                    await Download($"{app_home_remote}/blobs/{b.Sha1}", Path.Combine(Environment.GetEnvironmentVariable("NTX20_CACHEDIR"), b.Sha1));
                }

            }

            var root = Path.Combine(app_home_local, "apps", "ntx20", app.Version);
            Directory.CreateDirectory(root);
            foreach (var b in app.Blobs)
            {

                if (withCore20 || b.Platform == null || b.Platform.Trim().Length == 0)
                {
                    if (b.Archive == "tar")
                    {
                        var path = b.Path == null ? root : Path.Combine(root, b.Path);
                        Directory.CreateDirectory(path);
                        await Untar(Path.Combine(Environment.GetEnvironmentVariable("NTX20_CACHEDIR"), b.Sha1), path);
                    }
                    else
                    {
                        throw new NotImplementedException($"Not implemented archive type: {b.Archive}");
                    }
                }
            }
            File.Create(Path.Combine(app_home_local, "apps", "ntx20", app.Version, withCore20 ? ".core" : ".client")).Close();
            File.WriteAllText(Path.Combine(app_home_local, "apps", "ntx20", app.Version, ".manifest"),app.ToString());
            _logger.LogInformation($"Fetching completed {app.Version}");
            return app.Version;
        }

        async Task Download(string source, string target)
        {   
            if (File.Exists(target))
                return;
            _logger.LogInformation($"Downloading {source} => {target}");
            using var stream = api.io.LazyStream.Input(source);
            using var dest = new api.io.TempFileStream(target);
            await stream.CopyToAsync(dest);
            dest.Close();
        }
        async Task Untar(string source, string path)
        {
            using var inputStream = api.io.LazyStream.Input(source);
            using (var tarReader = new TarReader(inputStream))
            {

                TarEntry entry;
                while ((entry = tarReader.GetNextEntry()) != null)
                {
                    if (entry.EntryType is not TarEntryType.RegularFile)
                        continue;

                    var id = Path.Combine(path, entry.Name);
                    Directory.CreateDirectory(Path.GetDirectoryName(id));
                    _logger.LogInformation("Unpacking: {0}", id);
                    using var dest = new api.io.TempFileStream(id);
                    await entry.DataStream.CopyToAsync(dest);
                    dest.Close();
                }
                
            }
        }

        private int Run(string app_home_local, string version, string[] args)
        {
            var app_path = Path.Combine(app_home_local, "apps", "ntx20", version, "ntx20.app.dll");
            if (Environment.GetEnvironmentVariable("NTX20_APP_PATH") != null)
            {
                app_path = Environment.GetEnvironmentVariable("NTX20_APP_PATH");
            }

            var p = new Process();
            //p.StartInfo.CreateNoWindow = true;
            p.StartInfo.FileName = "dotnet";
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.Arguments = $"{app_path} {string.Join(" ", args)}";
            p.Start();
            p.WaitForExit();
            return p.ExitCode;
        }
    }


}
