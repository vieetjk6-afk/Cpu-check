using PcCheck.App.Infrastructure;
using PcCheck.Core.Localization;

namespace PcCheck.App.ViewModels;

/// <summary>Cơ sở cho các trang: tự dựng lại nội dung khi có kết quả quét mới hoặc đổi ngôn ngữ.</summary>
public abstract class PageViewModel : ObservableObject
{
    protected PageViewModel(MainViewModel main)
    {
        Main = main;
        State = main.State;
        State.ResultChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasResult));
            OnPropertyChanged(nameof(NoResult));
            SafeRebuild();
        };
        State.LiveUpdated += (_, _) => OnLive();
        Loc.Instance.LanguageChanged += (_, _) => SafeRebuild();
    }

    public MainViewModel Main { get; }

    public AppState State { get; }

    public bool HasResult => State.Result is not null;

    public bool NoResult => State.Result is null;

    protected abstract void Rebuild();

    /// <summary>Gọi khoảng 2 giây/lần khi có số liệu cảm biến mới.</summary>
    protected virtual void OnLive()
    {
    }

    private void SafeRebuild()
    {
        try
        {
            Rebuild();
        }
        catch (Exception ex)
        {
            Core.Util.Log.Error($"Dựng trang {GetType().Name} lỗi", ex);
        }
    }
}
