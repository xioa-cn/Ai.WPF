using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Ai.WPF.Models
{
    public class ChatMessage : INotifyPropertyChanged
    {
        private string _message;
        
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