using Archivio.Models;

namespace Archivio.ViewModels;

/// <summary>MainPageから設定ファイル形式と永続化実装を隠す小さな構成ルート。</summary>
public sealed class MainPageSettingsService
{
    public AppSettings LoadSettings() => SettingsManager.LoadSettings();
    public void SaveSettings(AppSettings settings) => SettingsManager.SaveSettings(settings);
    public HashSet<string> NormalizeVisibleListFieldIds(IEnumerable<string?>? ids)
        => SettingsManager.NormalizeVisibleListFieldIds(ids);
    public List<string> NormalizeListFieldOrder(IEnumerable<string>? ids)
        => SettingsManager.NormalizeListFieldOrder(ids);
    public List<string> NormalizeDetailFieldOrder(IEnumerable<string>? ids)
        => SettingsManager.NormalizeDetailFieldOrder(ids);
    public HashSet<string> NormalizeVisibleDetailFieldIds(IEnumerable<string?>? ids)
        => SettingsManager.NormalizeVisibleDetailFieldIds(ids);
}
