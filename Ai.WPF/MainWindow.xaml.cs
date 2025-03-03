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
        if (string.IsNullOrEmpty(userMessage))
            return;

        try
        {
            _isProcessing = true;

            // 添加用户消息
            Messages.Add(new ChatMessage { Message = userMessage, IsUser = true });

            // 清空输入框
            UserInputTextBox.Text = string.Empty;

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

            // 调用DeepSeek API获取流式回复
            await _deepSeekService.GetStreamingResponseAsync(
                Messages.ToList(),
                partialResponse =>
                {
                    // 在UI线程上更新消息
                    Dispatcher.Invoke(() =>
                    {
                        aiMessage.Message += partialResponse;


                        // 直接滚动到底部即可
                        ScrollToBottom();
                    });
                },
                _cancellationTokenSource.Token
            );
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

   
}