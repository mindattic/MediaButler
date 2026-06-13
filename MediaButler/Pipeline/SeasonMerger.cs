using MediaButler.Media;
using MediaButler.Settings;

namespace MediaButler.Pipeline;

/// <summary>
/// File-level merge of one season dump into an existing canonical season
/// folder, plus the sample-aware shell cleanup both the Rename and Move
/// stages need after consolidating content out of a torrent folder.
///
/// <para>Merging exists because real inboxes hold the SAME season twice in
/// different shapes ("Criminal Minds Season 2" with scene codes next to
/// "Criminal Minds Season 2 Complete WEB x264 [i_c]") — usually with
/// complementary episode subsets. Files move individually; a file stays put
/// when the target already has its exact name OR already has the same parsed
/// episode (so two rips of S03E01 surface as a human decision instead of
/// becoming a silent duplicate).</para>
/// </summary>
public static class SeasonMerger
{
    /// <summary>Outcome of a merge attempt.</summary>
    public sealed record MergeResult(int Moved, int Conflicts)
    {
        public bool FullyMerged => Conflicts == 0;
    }

    /// <summary>
    /// Merge every top-level media file (video + subtitle sidecars) of
    /// <paramref name="sourceFolder"/> into <paramref name="targetFolder"/>.
    /// Junk (txt/nfo/...) is left behind for shell cleanup. In dry-run nothing
    /// moves; the result reflects what WOULD happen.
    /// </summary>
    public static MergeResult MergeFiles(
        string sourceFolder, string targetFolder, int season,
        MediaButlerSettings settings, PipelineReport report)
    {
        var videoExts = new HashSet<string>(settings.VideoExtensions,    StringComparer.OrdinalIgnoreCase);
        var subExts   = new HashSet<string>(settings.SubtitleExtensions, StringComparer.OrdinalIgnoreCase);

        // Index the target once: file names + parsed episode numbers.
        var targetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targetEps   = new HashSet<int>();
        if (Directory.Exists(targetFolder))
        {
            foreach (var f in Directory.EnumerateFiles(targetFolder))
            {
                var fname = Path.GetFileName(f);
                targetNames.Add(fname);
                if (videoExts.Contains(Path.GetExtension(f)) &&
                    NameParser.ParseEpisodeNumberInSeason(fname, season) is { } ep)
                    targetEps.Add(ep);
            }
        }

        int moved = 0, conflicts = 0;
        foreach (var file in Directory.EnumerateFiles(sourceFolder))
        {
            var fname = Path.GetFileName(file);
            var ext = Path.GetExtension(file);
            var isVideo = videoExts.Contains(ext);
            if (!isVideo && !subExts.Contains(ext)) continue;          // junk stays for shell cleanup
            if (isVideo && NameParser.IsSampleName(fname)) continue;   // samples never merge

            var episode = isVideo ? NameParser.ParseEpisodeNumberInSeason(fname, season) : null;
            if (targetNames.Contains(fname) || (episode is { } e && targetEps.Contains(e)))
            {
                conflicts++;
                continue;
            }

            if (!settings.DryRun)
            {
                File.Move(file, Path.Combine(targetFolder, fname));
                AuditLog.Record(settings, settings.DryRun, "merge", file,
                    Path.Combine(targetFolder, fname), MediaKind.TvSeason);
            }
            targetNames.Add(fname);
            if (episode is { } moe) targetEps.Add(moe);
            moved++;
        }

        if (conflicts > 0)
        {
            report.RecordManual(sourceFolder, MediaKind.TvSeason,
                $"{conflicts} file(s) duplicate episodes already in {targetFolder} — pick the copy to keep");
        }
        return new MergeResult(moved, conflicts);
    }

    /// <summary>
    /// Delete a torrent shell folder after its media moved out, refusing when
    /// anything that could be real media remains: a non-sample video, a
    /// sample-named video above <see cref="MediaButlerSettings.SampleMaxBytes"/>,
    /// or more than <see cref="MediaButlerSettings.EmptyDeleteSafetyBytes"/> of
    /// other content. Returns true when the shell was (or in dry-run, would
    /// have been) deleted; on refusal sets <paramref name="refuseReason"/>.
    /// </summary>
    public static bool TryDeleteShell(
        string folder, MediaButlerSettings settings, out string? refuseReason)
    {
        refuseReason = null;
        if (!Directory.Exists(folder)) return true;

        var videoExts = new HashSet<string>(settings.VideoExtensions, StringComparer.OrdinalIgnoreCase);
        long junkBytes = 0;
        foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var fname = Path.GetFileName(f);
            long size;
            try { size = new FileInfo(f).Length; } catch { size = 0; }

            if (videoExts.Contains(Path.GetExtension(f)))
            {
                if (!NameParser.IsSampleName(fname))
                {
                    refuseReason = $"non-sample video still present: {fname}";
                    return false;
                }
                if (size > settings.SampleMaxBytes)
                {
                    refuseReason = $"sample-named video exceeds SampleMaxBytes: {fname} ({size / (1024.0 * 1024.0):F1} MB)";
                    return false;
                }
                continue; // bona fide sample — doesn't count against the junk floor
            }

            junkBytes += size;
            if (junkBytes > settings.EmptyDeleteSafetyBytes)
            {
                refuseReason = $"holds more than {settings.EmptyDeleteSafetyBytes / (1024.0 * 1024.0):F1} MB of non-video content";
                return false;
            }
        }

        if (!settings.DryRun) Directory.Delete(folder, recursive: true);
        return true;
    }
}
