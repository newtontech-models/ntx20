using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace ntx20.api.io
{
    public static partial class StreamProvider
    {
        private static readonly ILogger _logger = Logging.LoggerFactory.CreateLogger("ntx20.api.io.streamprovider");
        private static readonly HttpClientHandler _handler = new() { MaxConnectionsPerServer = 20 };

        public static Func<Task<Stream>> Input(string uri,CancellationToken token=default)
        {
            //preprocess
            if (uri.StartsWith("file://"))
            {
                uri = uri[7..];
            }
            

            if (uri=="-")
            {
                return () => Task.FromResult(Console.OpenStandardInput());
            }
            if (File.Exists(uri))
            {
                return () => Task.FromResult((Stream)File.OpenRead(uri));
            }
            if (uri.StartsWith("env://"))
            {
                return () => Task<Stream>.Run(
                    () =>
                    {
                        var variable = System.Environment.GetEnvironmentVariable(uri[6..]);
                        if (variable == null)
                            throw new Exception("Not existing variable: " + uri[6..]);
                        var m = new MemoryStream();
                        using (StreamWriter writer = new(m,Encoding.UTF8,4096,true))
                        {
                            writer.Write(variable);
                            writer.Flush();
                        }
                        m.Seek(0, SeekOrigin.Begin);
                        return m as Stream;
                    });
                
            }
            if (uri.StartsWith("text://"))
            {
                return () => Task.Run(
                    () =>
                    {
                        var variable = uri[7..];
                        if (variable == null)
                            throw new Exception("Not existing variable: " + uri[4..]);
                        var m = new MemoryStream();
                        using (StreamWriter writer = new(m, Encoding.UTF8, 4096, true))
                        {
                            writer.Write(variable);
                            writer.Flush();
                        }
                        m.Seek(0, SeekOrigin.Begin);
                        return m as Stream;
                    });

            }
            if(uri.StartsWith("http://") || uri.StartsWith("https://"))
            {
                return async () =>
                    {

                        void defer(HttpClient x) { x.Dispose(); }

                        var c = new HttpClient(_handler, false);
                        var response = await c.GetAsync(Uri.EscapeUriString(uri), HttpCompletionOption.ResponseHeadersRead,token);
                        if (!response.IsSuccessStatusCode)
                        {
                            defer(c);
                            response.EnsureSuccessStatusCode();
                        }
                        var stream = await response.Content.ReadAsStreamAsync();

                        return new DeferStream<HttpClient>(stream,c, defer);

                    };
            }

            if (uri.StartsWith("tcp://"))
            {
                var u = new Uri(uri);
                return async () =>
                {


                    void defer(TcpClient x) { x.Dispose(); }
                    var c = new TcpClient();
                    await c.ConnectAsync(u.Host, u.Port);
                    return new DeferStream<TcpClient>(c.GetStream(), c, defer) as Stream;

                };
            }
            if (uri.StartsWith("proc://"))
            {
                var match = Regex.Match(uri, @"^proc://(\S+)\s*(.*)");
                if (!match.Success)
                    throw new Exception($"Invalid uri: {uri}");
                var command = match.Groups[1].Value;
                var args = "";
                if (match.Groups.Count > 1)
                    args = match.Groups[2].Value;
                return () =>  Task.Run(
                    () =>
                    {
                        var p = new Process();

                        
                        p.StartInfo.CreateNoWindow = true;
                        p.StartInfo.FileName = command;
                        p.StartInfo.UseShellExecute = false;
                        p.StartInfo.Arguments = args;
                        p.StartInfo.RedirectStandardOutput = true;
                        p.StartInfo.RedirectStandardError = true;
                        p.ErrorDataReceived += (e, d) =>
                        {
                            _logger.LogDebug($"proc {command}, pid: {p.Id}, stderr: {d.Data}");
                        };
                        token.Register(() => p.Kill());
                       
                        if (!p.Start())
                        {
                            _logger.LogError($"process start failed: {command} args: {args}");
                            throw new Exception("Process start failed");
                        }
                        _logger.LogInformation($"process started: {command} pid: {p.Id}");
                        p.BeginErrorReadLine();
                        void defer(Process x)
                        {
                            try
                            {
                                if (!x.HasExited)
                                    x.Kill();
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"process failed: {command}, ex: {ex.Message}");
                            }
                            finally
                            {

                                _logger.LogInformation($"process exited: {command} code: {x.ExitCode}");
                            }

                        }

                        return new DeferStream<Process>(p.StandardOutput.BaseStream, p, defer) as Stream;
                    });
            }

            throw new Exception($"Can't parse: {uri}");
        }
        

    }
}
