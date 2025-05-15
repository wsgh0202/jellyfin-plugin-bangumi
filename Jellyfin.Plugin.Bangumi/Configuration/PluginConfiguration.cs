using System;
using System.Linq;
using System.Text.RegularExpressions;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Bangumi.Configuration;

public enum TranslationPreferenceType
{
    Original,
    Chinese
}

public class PluginConfiguration : BasePluginConfiguration
{
    public TranslationPreferenceType TranslationPreference { get; set; } = TranslationPreferenceType.Chinese;

    public TranslationPreferenceType PersonTranslationPreference { get; set; } = TranslationPreferenceType.Original;

    public int RequestTimeout { get; set; } = 5000;

    public string BaseServerUrl { get; set; } = "https://api.bgm.tv";

    public bool ReportPlaybackStatusToBangumi { get; set; } = true;

    public bool SkipNSFWPlaybackReport { get; set; } = true;

    public bool ReportManualStatusChangeToBangumi { get; set; } = false;

    public bool TrustExistedBangumiId { get; set; } = false;

    public bool UseBangumiSeasonTitle { get; set; } = true;

    public bool AlwaysReplaceEpisodeNumber { get; set; }

    public bool AlwaysGetTitleByAnitomySharp { get; set; }

    public bool AlwaysGetEpisodeByAnitomySharp { get; set; }

    public bool UseTestingSearchApi { get; set; }

    public int SeasonGuessMaxSearchCount { get; set; } = 2;

    public bool SortByFuzzScore { get; set; } = false;

    public bool RefreshRecentEpisodeWhenArchiveUpdate { get; set; } = false;

    public bool RefreshRatingWhenArchiveUpdate { get; set; } = false;

    public int DaysBeforeUsingArchiveData { get; set; } = 14;

    public int RatingUpdateMinInterval { get; set; } = 14;

    public string DefaultSpExcludeRegexFullPath => "";

    public string DefaultSpExcludeRegexFolderName => @"\b(SPs?|Specials?|PVs?|Previews?|Scans?|menus?|Fonts?|Extras?|CDs?|bonus|Music|Subs?|Subtitles?)\b
(特典|NCOP|NCED)";

    public string DefaultSpExcludeRegexFileName => @"\b(WEB予告|NCOP\d*|NCED\d*|menu|PV\d+)\b";

    private string _spExcludeRegexFullPath;
    public string SpExcludeRegexFullPath
    {
        get => _spExcludeRegexFullPath;
        set
        {
            _spExcludeRegexFullPath = CheckRegexes(value);
        }
    }

    private string _spExcludeRegexFolderName;
    public string SpExcludeRegexFolderName
    {
        get => _spExcludeRegexFolderName;
        set
        {
            _spExcludeRegexFolderName = CheckRegexes(value);
        }
    }

    private string _spExcludeRegexFileName;
    public string SpExcludeRegexFileName
    {
        get => _spExcludeRegexFileName;
        set
        {
            _spExcludeRegexFileName = CheckRegexes(value);
        }
    }

    public PluginConfiguration()
    {
        _spExcludeRegexFullPath = CheckRegexes(DefaultSpExcludeRegexFullPath);
        _spExcludeRegexFolderName = CheckRegexes(DefaultSpExcludeRegexFolderName);
        _spExcludeRegexFileName = CheckRegexes(DefaultSpExcludeRegexFileName);
    }

    /// <summary>
    /// 检查保存的正则表达式，去除空行
    /// </summary>
    /// <param name="regexes">用户保存内容</param>
    /// <returns>过滤后的配置</returns>
    private static string CheckRegexes(string regexes)
    {
        if (string.IsNullOrWhiteSpace(regexes))
            return string.Empty;

        var regexArray = regexes.Split("\n");

        return string.Join("\n", regexArray.Select(r => r.Trim())
            .Where(r => r.Length > 0));
    }

    /// <summary>
    /// 特典文件排除正则表达式匹配
    /// </summary>
    /// <param name="patterns">正则表达式配置</param>
    /// <param name="input">要匹配的文本</param>
    /// <param name="failedCallback">匹配报错回调，参数：当前匹配的正则表达式、异常对象</param>
    /// <returns></returns>
    public static bool MatchSpExcludeRegexes(string patterns, string input, Action<string, Exception>? failedCallback = null)
    {
        var patternFullPath = patterns.Split("\n");
        foreach (var item in patternFullPath)
        {
            if (string.IsNullOrEmpty(item)) continue;

            try
            {
                Regex regex = new Regex(item, RegexOptions.IgnoreCase);
                if (regex.IsMatch(input)) return true;
            }
            catch (Exception e)
            {
                failedCallback?.Invoke(item, e);
            }
        }

        return false;
    }
}
