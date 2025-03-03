using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Ai.WPF.Models;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Diagnostics;

namespace Ai.WPF.Services
{
    public class DeepSeekService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _apiUrl = "https://api.deepseek.com/v1/chat/completions";

        public DeepSeekService(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.Timeout = TimeSpan.FromMinutes(5); // 增加超时时间
        }

        public async Task<string> GetResponseAsync(List<ChatMessage> messages)
        {
            try
            {
                // 转换消息格式为DeepSeek API所需的格式
                var apiMessages = messages.Where(m => m.Message != "思考中...").Select(m => new
                {
                    role = m.IsUser ? "user" : "assistant",
                    content = m.Message
                }).ToList();

                // 创建请求体
                var requestBody = new
                {
                    model = "deepseek-chat", // 请替换为实际的模型名称
                    messages = apiMessages,
                    temperature = 0.7,
                    max_tokens = 1000
                };

                // 序列化请求体
                var content = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json");

                // 发送请求
                var response = await _httpClient.PostAsync(_apiUrl, content);
                response.EnsureSuccessStatusCode();

                // 解析响应
                var responseBody = await response.Content.ReadAsStringAsync();
                var responseObject = JsonSerializer.Deserialize<JsonElement>(responseBody);
                
                // 提取AI回复文本
                return responseObject.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            }
            catch (Exception ex)
            {
                return $"与AI通信时出错: {ex.Message}";
            }
        }

        public async Task GetStreamingResponseAsync(List<ChatMessage> messages, Action<string> onPartialResponse, CancellationToken cancellationToken = default)
        {
            try
            {
                // 转换消息格式为DeepSeek API所需的格式，排除"思考中..."消息
                var apiMessages = messages.Where(m => m.Message != "思考中...").Select(m => new
                {
                    role = m.IsUser ? "user" : "assistant",
                    content = m.Message
                }).ToList();

                // 创建请求体，添加stream=true参数
                var requestBody = new
                {
                    model = "deepseek-chat", // 请替换为实际的模型名称
                    messages = apiMessages,
                    temperature = 0.7,
                    max_tokens = 1000,
                    stream = true // 启用流式响应
                };

                // 序列化请求体
                var content = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json");

                // 发送请求
                var request = new HttpRequestMessage(HttpMethod.Post, _apiUrl)
                {
                    Content = content
                };

                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var reader = new StreamReader(stream))
                {
                    string line;
                    while ((line = await reader.ReadLineAsync()) != null && !cancellationToken.IsCancellationRequested)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        if (line.StartsWith("data: "))
                        {
                            var data = line.Substring(6); // 去掉 "data: " 前缀
                            
                            // 检查是否是流结束标记
                            if (data == "[DONE]")
                                break;

                            try
                            {
                                var jsonElement = JsonSerializer.Deserialize<JsonElement>(data);
                                
                                // 提取部分响应文本
                                if (jsonElement.TryGetProperty("choices", out var choices) && 
                                    choices.GetArrayLength() > 0 && 
                                    choices[0].TryGetProperty("delta", out var delta) && 
                                    delta.TryGetProperty("content", out var content1))
                                {
                                    var partialContent = content1.GetString();
                                    if (!string.IsNullOrEmpty(partialContent))
                                    {
                                        onPartialResponse(partialContent);
                                    }
                                }
                            }
                            catch (JsonException ex)
                            {
                                // 忽略JSON解析错误，继续处理下一行
                                Debug.WriteLine(ex.Message);

                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                onPartialResponse($"\n\n与AI通信时出错: {ex.Message}");
            }
        }
    }
} 