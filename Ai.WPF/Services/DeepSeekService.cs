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
                    model = "deepseek-reasoner",//"deepseek-chat", 
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

        public async Task GetStreamingResponseWithImageAsync(List<ChatMessage> messages, Action<string> onPartialResponse, CancellationToken cancellationToken = default)
        {
            try
            {
                // 找到最后一条带图片的用户消息
                var lastImageMessage = messages.LastOrDefault(m => m.IsUser && !string.IsNullOrEmpty(m.ImagePath));
                if (lastImageMessage == null || string.IsNullOrEmpty(lastImageMessage.ImagePath))
                {
                    // 如果没有图片，回退到普通文本处理
                    await GetStreamingResponseAsync(messages, onPartialResponse, cancellationToken);
                    return;
                }

                try
                {
                    // 准备图片数据 - 限制图片大小
                    var imageInfo = new FileInfo(lastImageMessage.ImagePath);
                    if (imageInfo.Length > 4 * 1024 * 1024) // 4MB限制
                    {
                        onPartialResponse("图片太大，请使用小于4MB的图片。");
                        return;
                    }

                    // 读取图片数据
                    byte[] imageData = File.ReadAllBytes(lastImageMessage.ImagePath);
                    string base64Image = Convert.ToBase64String(imageData);
                    string imageType = Path.GetExtension(lastImageMessage.ImagePath).TrimStart('.').ToLower();
                    
                    // 确保图片类型正确
                    if (imageType == "jpg") imageType = "jpeg";
                    
                    // 尝试方案1：使用OpenAI格式（许多API提供商兼容这种格式）
                    var requestBody = new
                    {
                        model = "deepseek-chat", // 尝试使用标准聊天模型
                        messages = new[]
                        {
                            new { role = "user", content = lastImageMessage.Message + "\n[图片已上传]" }
                        },
                        temperature = 0.7,
                        max_tokens = 2000,
                        stream = true
                    };

                    // 序列化请求体
                    var jsonOptions = new JsonSerializerOptions
                    {
                        WriteIndented = false,
                        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                    };
                    
                    var jsonContent = JsonSerializer.Serialize(requestBody, jsonOptions);
                    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    // 添加调试信息
                    Debug.WriteLine($"发送请求: {jsonContent}");

                    // 发送请求
                    var request = new HttpRequestMessage(HttpMethod.Post, _apiUrl)
                    {
                        Content = content
                    };

                    var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    
                    // 如果请求失败，尝试方案2
                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        Debug.WriteLine($"方案1失败: {response.StatusCode}, {errorContent}");
                        
                        // 方案2：尝试使用不同的消息格式
                       var requestBody1 = new
                        {
                            model = "deepseek-chat",
                            messages = new object[]
                            {
                                new { role = "user", content = lastImageMessage.Message }
                            },
                            temperature = 0.7,
                            max_tokens = 2000,
                            stream = true
                        };
                        
                        jsonContent = JsonSerializer.Serialize(requestBody1, jsonOptions);
                        content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                        
                        Debug.WriteLine($"尝试方案2: {jsonContent}");
                        
                        request = new HttpRequestMessage(HttpMethod.Post, _apiUrl)
                        {
                            Content = content
                        };
                        
                        response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                        
                        if (!response.IsSuccessStatusCode)
                        {
                            errorContent = await response.Content.ReadAsStringAsync();
                            Debug.WriteLine($"方案2失败: {response.StatusCode}, {errorContent}");
                            onPartialResponse($"\n\n与AI通信时出错: 无法处理图片。将仅使用文本进行回复。\n\n");
                            
                            // 回退到纯文本处理
                            await GetStreamingResponseAsync(messages, onPartialResponse, cancellationToken);
                            return;
                        }
                    }
                    
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
                                        choices.GetArrayLength() > 0)
                                    {
                                        if (choices[0].TryGetProperty("delta", out var delta) && 
                                            delta.TryGetProperty("content", out var content1))
                                        {
                                            var partialContent = content1.GetString();
                                            if (!string.IsNullOrEmpty(partialContent))
                                            {
                                                onPartialResponse(partialContent);
                                            }
                                        }
                                    }
                                }
                                catch (JsonException ex)
                                {
                                    // 忽略JSON解析错误，继续处理下一行
                                    Debug.WriteLine($"JSON解析错误: {ex.Message}, 数据: {data}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"处理图片错误: {ex.Message}");
                    onPartialResponse($"\n\n处理图片时出错: {ex.Message}");
                    
                    // 回退到纯文本处理
                    var textOnlyMessages = messages.Select(m => new ChatMessage 
                    { 
                        Message = m.Message, 
                        IsUser = m.IsUser,
                        Timestamp = m.Timestamp
                    }).ToList();
                    
                    await GetStreamingResponseAsync(textOnlyMessages, onPartialResponse, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"总体错误: {ex.Message}");
                onPartialResponse($"\n\n与AI通信时出错: {ex.Message}");
            }
        }
    }
} 