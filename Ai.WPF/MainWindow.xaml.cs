using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Ai.WPF.Models;
using Ai.WPF.Services;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Documents;
using System.IO;
using System.Windows.Media.Imaging;

using System.Linq;

namespace Ai.WPF;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public ObservableCollection<ChatMessage> Messages { get; set; }
    private DeepSeekService _deepSeekService;
    private bool _isProcessing = false;
    private CancellationTokenSource _cancellationTokenSource;
    private ScrollViewer _chatScrollViewer;
    private string _currentImagePath;
    private BitmapImage _currentImage;

    public MainWindow()
    {
        InitializeComponent();

        Messages = new ObservableCollection<ChatMessage>();
        ChatHistoryList.ItemsSource = Messages;

        // 初始化DeepSeek服务 - 替换为您的API密钥
        _deepSeekService = new DeepSeekService("sk-79d3ad1aadf84ae8a7d7c015908aeef3");

        // 绑定事件
        SendButton.Click += SendButton_Click;
        UserInputTextBox.KeyDown += UserInputTextBox_KeyDown;

        // 添加欢迎消息
        Messages.Add(new ChatMessage { Message = "你好，我是DeepSeek AI助手，有什么可以帮助你的？", IsUser = false });

        // 在ItemsControl加载完成后获取ScrollViewer
        ChatHistoryList.Loaded += (s, e) =>
        {
            _chatScrollViewer = GetScrollViewer(ChatHistoryList);
        };
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        await SendMessageAsync();
    }

    private async void UserInputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
        {
            e.Handled = true;
            await SendMessageAsync();
        }
    }
    private async void UserInputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
        {
            e.Handled = true; // 阻止默认的回车行为

            if (string.IsNullOrWhiteSpace(UserInputTextBox.Text))
                return;

            await SendMessageAsync();
        }
    }
    private async Task SendMessageAsync()
    {
        if (_isProcessing)
            return;

        string userMessage = UserInputTextBox.Text.Trim();
        bool hasImage = _currentImage != null;
        
        // 如果没有文本且没有图片，则不发送
        if (string.IsNullOrEmpty(userMessage) && !hasImage)
            return;

        try
        {
            _isProcessing = true;

            // 添加用户消息（包含图片）
            var userChatMessage = new ChatMessage 
            { 
                Message = userMessage, 
                IsUser = true,
                Image = _currentImage,
                ImagePath = _currentImagePath
            };
            Messages.Add(userChatMessage);

            // 清空输入框和图片预览
            UserInputTextBox.Text = string.Empty;
            RemoveImage_Click(null, null);

            // 滚动到底部确保用户消息可见
            ScrollToBottom();

            // 创建AI回复消息（初始为空）
            var aiMessage = new ChatMessage { Message = "", IsUser = false };
            Messages.Add(aiMessage);

            // 再次滚动到底部确保AI消息框可见
            ScrollToBottom();

            // 取消之前的请求（如果有）
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();

            // 调用DeepSeek API获取流式回复（包含图片处理）
            if (hasImage)
            {
                // 使用带图片的API调用
                await _deepSeekService.GetStreamingResponseWithImageAsync(
                    Messages.ToList(),
                    partialResponse =>
                    {
                        // 在UI线程上更新消息
                        Dispatcher.Invoke(() =>
                        {
                            aiMessage.Message += partialResponse;
                            ScrollToBottom();
                        });
                    },
                    _cancellationTokenSource.Token
                );
            }
            else
            {
                // 使用普通文本API调用
                await _deepSeekService.GetStreamingResponseAsync(
                    Messages.ToList(),
                    partialResponse =>
                    {
                        // 在UI线程上更新消息
                        Dispatcher.Invoke(() =>
                        {
                            aiMessage.Message += partialResponse;
                            ScrollToBottom();
                        });
                    },
                    _cancellationTokenSource.Token
                );
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isProcessing = false;
        }
    }

    private void ScrollToBottom()
    {
        if (_chatScrollViewer != null)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _chatScrollViewer.ScrollToBottom();
            }, DispatcherPriority.Loaded);
        }
    }

    // 更可靠的查找ScrollViewer的方法
    private ScrollViewer GetScrollViewer(DependencyObject depObj)
    {
        return ChatScrollViewer;
    }

    // 开启新对话按钮点击事件
    private void NewChatButton_Click(object sender, RoutedEventArgs e)
    {
        // 取消当前请求（如果有）
        _cancellationTokenSource?.Cancel();

        Messages.Clear();
        Messages.Add(new ChatMessage { Message = "你好，我是DeepSeek AI助手，有什么可以帮助你的？", IsUser = false });
    }

    // 窗口关闭时取消所有请求
    protected override void OnClosed(EventArgs e)
    {
        _cancellationTokenSource?.Cancel();
        base.OnClosed(e);
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string text)
        {
            try
            {
                // 使用完全限定名称调用Clipboard类
                System.Windows.Clipboard.SetText(text);

                // 可选：显示复制成功提示
                MessageBox.Show("文本已复制到剪贴板", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"复制失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void InputBox_DragOver(object sender, DragEventArgs e)
    {
        // 只接受文件拖放
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0)
            {
                string extension = Path.GetExtension(files[0]).ToLower();
                // 只接受图片文件
                if (extension == ".jpg" || extension == ".jpeg" || extension == ".png" || extension == ".gif" || extension == ".bmp")
                {
                    e.Effects = DragDropEffects.Copy;
                    e.Handled = true;
                    return;
                }
            }
        }
        
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void InputBox_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0)
            {
                string filePath = files[0];
                string extension = Path.GetExtension(filePath).ToLower();
                
                // 检查是否是图片文件
                if (extension == ".jpg" || extension == ".jpeg" || extension == ".png" || extension == ".gif" || extension == ".bmp")
                {
                    try
                    {
                        // 加载图片
                        BitmapImage image = new BitmapImage();
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.UriSource = new Uri(filePath);
                        image.EndInit();
                        
                        // 保存当前图片信息
                        _currentImagePath = filePath;
                        _currentImage = image;
                        
                        // 显示图片预览
                        ImagePreview.Source = image;
                        ImageNameText.Text = Path.GetFileName(filePath);
                        ImagePreviewBorder.Visibility = Visibility.Visible;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"加载图片失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }
    }

    private void RemoveImage_Click(object sender, RoutedEventArgs e)
    {
        // 清除当前图片
        _currentImagePath = null;
        _currentImage = null;
        ImagePreview.Source = null;
        ImagePreviewBorder.Visibility = Visibility.Collapsed;
    }
}