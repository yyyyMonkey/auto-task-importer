using M365TfsSync.Models;

namespace M365TfsSync.ViewModels;

public class CalendarEventViewModel : ViewModelBase
{
    private bool _isSelected;
    private bool _isDuplicate;
    private string _duplicateWarning = string.Empty;

    public CalendarEvent Event { get; }

    public CalendarEventViewModel(CalendarEvent calendarEvent)
    {
        Event = calendarEvent ?? throw new ArgumentNullException(nameof(calendarEvent));
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isDuplicate) return;
            if (SetProperty(ref _isSelected, value))
                SelectionChanged?.Invoke();
        }
    }

    /// <summary>勾選狀態變更時通知外部（供 MainViewModel 更新 ConfirmCommand）</summary>
    public event Action? SelectionChanged;

    public bool IsDuplicate
    {
        get => _isDuplicate;
        set
        {
            if (SetProperty(ref _isDuplicate, value))
            {
                // IsDuplicate 變更時，同步更新 IsSelectable 與 IsSelected
                OnPropertyChanged(nameof(IsSelectable));
                if (value)
                {
                    // 標記為重複時，強制取消勾選
                    _isSelected = false;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }
    }

    public string DuplicateWarning
    {
        get => _duplicateWarning;
        set => SetProperty(ref _duplicateWarning, value);
    }

    /// <summary>
    /// 勾選框是否可用，恆等於 !IsDuplicate
    /// </summary>
    public bool IsSelectable => !_isDuplicate;

    // 便利屬性，直接從 Event 取得
    public string Subject => Event.Subject;
    public DateTime StartTime => Event.StartTime;
    public DateTime EndTime => Event.EndTime;
}
