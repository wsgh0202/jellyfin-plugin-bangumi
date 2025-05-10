using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Bangumi.Providers;

public class SeasonProvider(BangumiApi api, Logger<EpisodeProvider> log, ILibraryManager libraryManager)
    : IRemoteMetadataProvider<Season, SeasonInfo>, IHasOrder
{
    private static PluginConfiguration Configuration => Plugin.Instance!.Configuration;

    public int Order => -5;

    public string Name => Constants.ProviderName;

    private static readonly Dictionary<int, string> chineseOrdinalChars = new()
    {
        { 1, "一" },
        { 2, "二" },
        { 3, "三" },
        { 4, "四" },
        { 5, "五" },
        { 6, "六" },
        { 7, "七" },
        { 8, "八" },
        { 9, "九" },
        { 10, "十" },
    };

    public async Task<MetadataResult<Season>> GetMetadata(SeasonInfo info, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Subject? subject = null;

        if (string.IsNullOrEmpty(info.Path))
            return new MetadataResult<Season>();

        var baseName = Path.GetFileName(info.Path);
        var result = new MetadataResult<Season> { ResultLanguage = Constants.Language };
        var localConfiguration = await LocalConfiguration.ForPath(info.Path);

        var seasonPath = Path.GetDirectoryName(info.Path);

        var subjectId = 0;
        if (localConfiguration.Id != 0)
        {
            subjectId = localConfiguration.Id;
        }
        else if (int.TryParse(baseName.GetAttributeValue("bangumi"), out var subjectIdFromAttribute))
        {
            subjectId = subjectIdFromAttribute;
        }
        else if (int.TryParse(info.ProviderIds.GetOrDefault(Constants.ProviderName), out var subjectIdFromInfo))
        {
            subjectId = subjectIdFromInfo;
        }
        else if (info.IndexNumber == 1 &&
                 int.TryParse(info.SeriesProviderIds.GetOrDefault(Constants.ProviderName), out var subjectIdFromParent))
        {
            subjectId = subjectIdFromParent;
        }
        else if (seasonPath is not null && libraryManager.FindByPath(seasonPath, true) is Series series)
        {
            log.Info($"Guessing season id by folder name:  {baseName}");
            subject = await SearchSubjectByFolderName(baseName, cancellationToken);

            if (subject != null)
            {
                subjectId = subject.Id;
                log.Info("Guessed result: {Name} (#{ID})", subject.Name, subject.Id);
            }
            else
            {
                var previousSeason = series.Children
                    // Search "Season 2" for "Season 1" and "Season 2 Part X"
                    .Where(x => x.IndexNumber == info.IndexNumber - 1 || x.IndexNumber == info.IndexNumber)
                    .MaxBy(x => int.Parse(x.GetProviderId(Constants.ProviderName) ?? "0"));
                if (previousSeason?.Path == info.Path)
                {
                    //This is the first season to be matched, which means season 1 and any other possible previous season is missing. We can just try match it by name.
                    string[] searchNames = [$"{series.Name} 第{chineseOrdinalChars[info.IndexNumber ?? 1]}季", $"{series.Name} Season {info.IndexNumber}"];
                    foreach (var searchName in searchNames)
                    {
                        log.Info($"Guessing season id by name:  {searchName}");
                        var searchResult = await api.SearchSubject(searchName, cancellationToken);
                        if (int.TryParse(info.SeriesProviderIds.GetOrDefault(Constants.ProviderName), out var parentId))
                        {
                            searchResult = searchResult.Where(x => x.Id != parentId);
                        }
                        if (info.Year != null)
                        {
                            searchResult = searchResult.Where(x => x.ProductionYear == null || x.ProductionYear == info.Year?.ToString());
                        }
                        if (searchResult.Any())
                            subjectId = searchResult.First().Id;
                    }
                    log.Info("Guessed result: {Name} (#{ID})", subject?.Name, subject?.Id);
                }
                if (int.TryParse(previousSeason?.GetProviderId(Constants.ProviderName), out var previousSeasonId) && previousSeasonId > 0)
                {
                    log.Info("Guessing season id from previous season #{ID}", previousSeasonId);
                    subject = await api.SearchNextSubject(previousSeasonId, cancellationToken);
                    if (subject != null)
                    {
                        log.Info("Guessed result: {Name} (#{ID})", subject.Name, subject.Id);
                        subjectId = subject.Id;
                    }
                }
            }
        }

        if (subjectId <= 0)
            return result;

        subject ??= await api.GetSubject(subjectId, cancellationToken);

        // return if subject still not found
        if (subject == null)
            return result;

        result.Item = new Season();
        result.HasMetadata = true;

        result.Item.ProviderIds.Add(Constants.ProviderName, subject.Id.ToString());
        result.Item.CommunityRating = subject.Rating?.Score;
        if (Configuration.UseBangumiSeasonTitle)
        {
            result.Item.Name = subject.Name;
            result.Item.OriginalTitle = subject.OriginalName;
        }

        result.Item.Overview = string.IsNullOrEmpty(subject.Summary) ? null : subject.Summary;
        result.Item.Tags = subject.PopularTags.ToArray();
        result.Item.Genres = subject.GenreTags.ToArray();

        if (DateTime.TryParse(subject.AirDate, out var airDate))
        {
            result.Item.PremiereDate = airDate;
            result.Item.ProductionYear = airDate.Year;
        }

        if (subject.ProductionYear?.Length == 4)
            result.Item.ProductionYear = int.Parse(subject.ProductionYear);

        result.Item.HomePageUrl = subject.OfficialWebSite;
        result.Item.EndDate = subject.EndDate;

        if (subject.IsNSFW)
            result.Item.OfficialRating = "X";

        (await api.GetSubjectPersonInfos(subject.Id, cancellationToken)).ToList().ForEach(result.AddPerson);
        (await api.GetSubjectCharacters(subject.Id, cancellationToken)).ToList().ForEach(result.AddPerson);

        return result;
    }

    private async Task<Subject?> SearchSubjectByFolderName(string folderName, CancellationToken cancellationToken)
    {
        var searchName = GetBangumiNameFromFolderName(folderName);
        var subjects = await api.SearchSubjectSorted(searchName, SubjectType.Anime, cancellationToken);
        if (subjects == null || !subjects.Any()) return null;

        var bestMatched = subjects.FirstOrDefault();
        // 相似度百分比，大于等于该值则认为匹配成功
        int matchPercent = 80;

        return bestMatched.Item2 >= matchPercent ? bestMatched.Item1 : null;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeasonInfo searchInfo, CancellationToken cancellationToken)
    {
        return Task.FromResult(Enumerable.Empty<RemoteSearchResult>());
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return api.GetHttpClient().GetAsync(url, cancellationToken);
    }

    /// <summary>
    /// 获取番剧名称
    /// </summary>
    /// <param name="foldername">目录名</param>
    /// <returns>番剧名称</returns>
    private static string GetBangumiNameFromFolderName(string foldername)
    {
        // 常见的目录格式中，番剧名称通常为非括号内的内容
        // 部分目录格式则是将包括番剧名称在内的元数据都用括号分割，通常情况番剧名称在第二个括号内容里

        Dictionary<char, char> brackets = new()
        {
            ['['] = ']',
            ['('] = ')',
            ['【'] = '】',
            ['（'] = '）',
        };

        // 括号内的内容
        List<string> bracketContent = [];
        // 当前分块内容
        string current = string.Empty;
        // 括号栈
        Stack<char> stack = new();

        foreach (char c in foldername)
        {
            if (brackets.ContainsKey(c)) // 匹配到左括号
            {
                if (stack.Count == 0)
                {
                    current = current.Trim();

                    // 获取到非括号内内容，直接返回
                    if (current.Length > 0)
                    {
                        return current;
                    }
                }

                stack.Push(c);
            }
            else if (brackets.ContainsValue(c)) // 匹配到右括号
            {
                if (stack.Count > 0 && c == brackets[stack.Peek()]) // 嵌套括号匹配
                {
                    stack.Pop();

                    // 匹配到最外层括号，记录其内容
                    if (stack.Count == 0)
                    {
                        current += c;

                        bracketContent.Add(current);
                        current = string.Empty;

                        continue;
                    }
                }
            }

            current += c;
        }

        // 匹配不到非括号内内容，默认返回第2个括号内的内容
        return bracketContent.Count > 1 ? bracketContent[1] : bracketContent[0];
    }
}
