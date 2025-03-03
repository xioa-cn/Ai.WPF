using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace Ai.WPF.Models
{
    public class ChatMessage : INotifyPropertyChanged
    {
        private string _message;
        private BitmapImage _image;
        
        public string Message
        {
            get => _message;
            set
            {
                if (_message != value)
                {
                    _message = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public bool IsUser { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        
        // 新增：图片属性
        public BitmapImage Image
        {
            get => _image;
            set
            {
                if (_image != value)
                {
                    _image = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasImage));
                }
            }
        }
        
        // 新增：图片路径属性（用于API调用）
        public string ImagePath { get; set; }
        
        // 新增：判断是否有图片
        public bool HasImage => Image != null;
        
        // 用于UI显示的属性
        public string SenderName => IsUser ? "你" : "DeepSeek";
        
        // 实现INotifyPropertyChanged接口
        public event PropertyChangedEventHandler PropertyChanged;
        
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 