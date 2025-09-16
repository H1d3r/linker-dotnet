﻿using linker.libs.extends;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace linker.libs.web
{
    /// <summary>
    /// 本地web管理端服务器
    /// </summary>
    public class WebServer : IWebServer
    {
        private string root = "";
        private string password = Helper.GlobalString;
        protected readonly Dictionary<string, PluginPathCacheInfo> plugins = new();

        private readonly IWebServerFileReader fileReader;
        public WebServer(IWebServerFileReader fileReader)
        {
            this.fileReader = fileReader;
        }

        /// <summary>
        /// 开启web
        /// </summary>
        public void Start(int port, string root, string password)
        {
            this.root = root;
            this.password = password;
            Task.Factory.StartNew(async () =>
            {
                try
                {
                    HttpListener http = new HttpListener();
                    http.IgnoreWriteExceptions = true;
                    http.Prefixes.Add($"http://+:{port}/");
                    http.Start();

                    while (true)
                    {
                        try
                        {
                            HttpListenerContext context = await http.GetContextAsync();
                            if (context.Request.IsWebSocketRequest)
                            {
                                HttpListenerWebSocketContext wsContext = await context.AcceptWebSocketAsync(null);
                                HandleWs(wsContext.WebSocket);
                            }
                            else
                            {
                                HandleWeb(context);
                            }
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoggerHelper.Instance.Error(ex);
                }
            }, TaskCreationOptions.LongRunning);
        }

        private void HandleWeb(HttpListenerContext context)
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;
            try
            {
                response.Headers.Set("Server", Helper.GlobalString);

                string path = request.Url.AbsolutePath;
                //默认页面
                if (path == "/") path = "index.html";

                Memory<byte> memory = Helper.EmptyArray;
                DateTime last = DateTime.Now;
                try
                {
                    memory = fileReader.Read(root, path, out last);
                    if (memory.Length > 0)
                    {
                        response.ContentLength64 = memory.Length;
                        response.ContentType = GetContentType(path);
                        if (OperatingSystem.IsAndroid())
                        {
                            response.Headers.Set("Last-Modified", last.ToString("yyyy-MM-dd HH:mm:ss"));
                        }
                        else
                        {
                            response.Headers.Set("Last-Modified", last.ToString());
                        }
                        response.OutputStream.Write(memory.Span);
                        response.OutputStream.Flush();
                        response.OutputStream.Close();
                    }
                    else
                    {
                        response.StatusCode = (int)HttpStatusCode.NotFound;
                    }
                }
                catch (Exception ex)
                {
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(ex + $"");
                    response.ContentLength64 = bytes.Length;
                    response.ContentType = "text/plain; charset=utf-8";
                    response.OutputStream.Write(bytes, 0, bytes.Length);
                    response.OutputStream.Flush();
                    response.OutputStream.Close();
                }
            }
            catch (Exception)
            {
                response.StatusCode = (int)HttpStatusCode.BadRequest;
            }

            response.Close();
        }
        private Dictionary<string, string> types = new Dictionary<string, string> {
            { ".webp","image/webp"},
            { ".png","image/png"},
            { ".jpg","image/jpg"},
            { ".jpeg","image/jpeg"},
            { ".gif","image/gif"},
            { ".svg","image/svg+xml"},
            { ".ico","image/x-icon"},
            { ".js","text/javascript; charset=utf-8"},
            { ".html","text/html; charset=utf-8"},
            { ".css","text/css; charset=utf-8"},
            { ".zip","application/zip"},
            { ".pac","application/x-ns-proxy-autoconfig; charset=utf-8"},
        };
        private string GetContentType(string path)
        {
            string ext = Path.GetExtension(path);
            if (types.TryGetValue(ext,out string value))
            {
                return value;
            }
            return "application/octet-stream";
        }

        public void SetPassword(string password)
        {
            this.password = password;
        }
        private async void HandleWs(WebSocket websocket)
        {
            using IMemoryOwner<byte> buffer = MemoryPool<byte>.Shared.Rent(8*1024);
            try
            {
                ValueWebSocketReceiveResult result = await websocket.ReceiveAsync(buffer.Memory, CancellationToken.None);
                if (result.MessageType != WebSocketMessageType.Text)
                {
                    await websocket.CloseAsync(WebSocketCloseStatus.ProtocolError, "password fail", CancellationToken.None);
                    return;
                }
                ApiControllerRequestInfo req = Encoding.UTF8.GetString(buffer.Memory.Slice(0, result.Count).Span).DeJson<ApiControllerRequestInfo>();
                if (req.Path != "password" || req.Content != this.password)
                {
                    await websocket.CloseAsync(WebSocketCloseStatus.ProtocolError, "password fail", CancellationToken.None);
                    return;
                }
                await websocket.SendAsync(new ApiControllerResponseInfo
                {
                    Code = ApiControllerResponseCodes.Success,
                    Path = req.Path,
                    RequestId = req.RequestId,
                    Content = "password ok",
                }.ToJson().ToBytes(), WebSocketMessageType.Text, true, CancellationToken.None);

                while (websocket.State == WebSocketState.Open)
                {
                    try
                    {
                        result = await websocket.ReceiveAsync(buffer.Memory, CancellationToken.None);
                        switch (result.MessageType)
                        {
                            case WebSocketMessageType.Text:
                                {
                                    req = Encoding.UTF8.GetString(buffer.Memory.Slice(0, result.Count).Span).DeJson<ApiControllerRequestInfo>();
                                    req.Connection = websocket;
                                    ApiControllerResponseInfo resp = await OnMessage(req);
                                    await websocket.SendAsync(resp.ToJson().ToBytes(), WebSocketMessageType.Text, true, CancellationToken.None);
                                }
                                break;
                            case WebSocketMessageType.Binary:
                                break;
                            case WebSocketMessageType.Close:
                                await websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", CancellationToken.None);
                                break;
                            default:
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggerHelper.Instance.Error($"{req.Path}->{ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.Instance.Error(ex);
            }
        }
        private async Task<ApiControllerResponseInfo> OnMessage(ApiControllerRequestInfo model)
        {
            model.Path = model.Path.ToLower();
            if (plugins.TryGetValue(model.Path, out PluginPathCacheInfo plugin) == false)
            {
                return new ApiControllerResponseInfo
                {
                    Content = $"{model.Path} not exists",
                    RequestId = model.RequestId,
                    Path = model.Path,
                    Code = ApiControllerResponseCodes.NotFound
                };
            }
            if (plugin.HasAccess(plugin.Access) == false)
            {
                return new ApiControllerResponseInfo
                {
                    Content = "no permission",
                    RequestId = model.RequestId,
                    Path = model.Path,
                    Code = ApiControllerResponseCodes.Error
                };
            }

            try
            {
                ApiControllerParamsInfo param = new ApiControllerParamsInfo
                {
                    RequestId = model.RequestId,
                    Content = model.Content,
                    Connection = model.Connection
                };
                dynamic resultAsync = plugin.Method.Invoke(plugin.Target, new object[] { param });
                object resultObject = null;
                if (plugin.IsVoid == false)
                {
                    if (plugin.IsTask)
                    {
                        await resultAsync.ConfigureAwait(false);
                        if (plugin.IsTaskResult)
                        {
                            resultObject = resultAsync.Result;
                        }
                    }
                    else
                    {
                        resultObject = resultAsync;
                    }
                }
                return new ApiControllerResponseInfo
                {
                    Code = param.Code,
                    Content = param.Code != ApiControllerResponseCodes.Error ? resultObject : param.ErrorMessage,
                    RequestId = model.RequestId,
                    Path = model.Path,
                };
            }
            catch (Exception ex)
            {
                LoggerHelper.Instance.Error($"{model.Path} -> {ex.Message}");
                return new ApiControllerResponseInfo
                {
                    Content = ex.Message,
                    RequestId = model.RequestId,
                    Path = model.Path,
                    Code = ApiControllerResponseCodes.Error
                };
            }
        }
    }


    public interface IWebServerFileReader
    {
        public byte[] Read(string root, string fileName, out DateTime lastModified);
    }
    public sealed class WebServerFileReader : IWebServerFileReader
    {
        public byte[] Read(string root, string fileName, out DateTime lastModified)
        {
            fileName = Path.Join(root, fileName);
            lastModified = File.GetLastWriteTimeUtc(fileName);
            return File.ReadAllBytes(fileName);
        }
    }

    public struct PluginPathCacheInfo
    {
        /// <summary>
        /// 对象
        /// </summary>
        public object Target { get; set; }
        /// <summary>
        /// 方法
        /// </summary>
        public MethodInfo Method { get; set; }
        /// <summary>
        /// 是否void
        /// </summary>
        public bool IsVoid { get; set; }
        /// <summary>
        /// 是否task
        /// </summary>
        public bool IsTask { get; set; }
        /// <summary>
        /// 是否task result
        /// </summary>
        public bool IsTaskResult { get; set; }

        public int Access { get; set; }
        public Func<int, bool> HasAccess { get; set; }
    }
}
