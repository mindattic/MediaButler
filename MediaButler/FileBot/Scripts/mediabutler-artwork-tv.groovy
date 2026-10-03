#!/usr/bin/env -S filebot -script

// Replaces FileBot's bundled fn:artwork.tvdb for MediaButler's flat TV layout
// ({Show} - Season NN directly under TvDestination, no parent {Show} folder —
// see docs/BIBLE.md, MB-A10). That script picks "seriesDir" vs "seasonDir" by
// comparing the season folder's parent (the shared TV root, same for every
// show) against the season folder itself for name-similarity to the series —
// the season folder always wins, so seriesDir == seasonDir and the script's
// own `if (seasonDir != seriesDir)` guard skips its season-specific poster
// fetch. Every season ends up with the same series-level poster.jpg.
//
// This script fetches the series-wide extras the same way, then explicitly
// overwrites poster.jpg / folder.jpg / banner.jpg / landscape.jpg with
// season-specific artwork. When TheTVDB has no art for that season, it prints
// a marker line MediaButler's FileBotResult.LooksLikeSeasonArtFallback looks
// for, so the pipeline can superimpose a season number on the series poster
// instead of leaving two seasons visually identical.
//
// The TheTVDB artwork/nfo helpers below are inlined from FileBot's own
// lib/htpc.groovy (fetchSeriesBanner, fetchSeriesFanart, fetchSeriesNfo,
// getTVDBID, fetchSeriesArtworkAndNfo) rather than loaded via include('lib/htpc') --
// that only resolves relative to this script's own directory when FileBot runs
// a local file (as opposed to a bundled fn: script), so it can't find FileBot's
// own bundled copy and fails with "No Such File: .../Scripts/lib/htpc.groovy".

if (args.size() == 0) {
	die "Illegal usage: no input"
}

def fetchSeriesBanner(outputFile, series, bannerType, bannerType2, season, override, locale) {
	if (outputFile.exists() && !override) {
		return outputFile
	}
	def artwork = series.getArtwork(bannerType, locale)
	if (artwork == null) {
		return null
	}
	def banner = artwork.find{ it.matches(bannerType2, season) }
	if (banner == null) {
		return null
	}
	log.finest "Fetching $outputFile => $banner"
	return banner.url.cache().saveAs(outputFile)
}

def fetchSeriesFanart(outputFile, series, type, season, override, locale) {
	if (outputFile.exists() && !override) {
		return outputFile
	}
	def artwork = FanartTV.getArtwork(series.id, "tv", locale)
	def fanart = artwork.find{ it.matches(type, season) }
	if (fanart == null) {
		return null
	}
	log.finest "Fetching $outputFile => $fanart"
	return fanart.url.cache().saveAs(outputFile)
}

def fetchSeriesNfo(outputFile, s, locale) {
	log.finest "Generate Series NFO: $s.name [$s]"
	def db = s.database.match('TheMovieDB':'tmdb', 'TheTVDB':'tvdb', 'AniDB':'anidb', 'TVmaze':'tvmaze')
	def xml = XML {
		tvshow {
			id(s.id)
			title(s.name)
			sorttitle([s.name, s.startDate].findResults{ it?.toString()?.sortName() }.join(' :: '))
			year(s.startDate?.year)
			premiered(s.startDate)
			mpaa(s.certification)
			plot(s.overview)
			runtime(s.runtime)
			ratings {
				rating(name: db, max: '10', default: 'true') {
					value(s.rating)
					votes(s.ratingCount)
				}
			}
			status(s.status)
			studio(s.network)
			episodeguide(s.id)
			s.episodes.collectEntries{ e -> [e.episode ? e.season : 0, e.group] }.each{ seasonNumber, seasonName ->
				if (seasonName) {
					namedseason(number: seasonNumber, seasonName)
				}
			}
			s.genres.each{ g -> genre(g) }
			s.country.each{ c -> country(c) }
			s.artwork.findAll{ a -> a.matches(/posters/) }.take(1).each{ a -> thumb(aspect: 'poster', a.url) }
			s.artwork.findAll{ a -> a.matches(/logos/) }.take(1).each{ a -> thumb(aspect: 'clearlogo', a.url) }
			s.artwork.findAll{ a -> a.matches(/backdrops/) }.take(1).each{ a -> fanart { thumb(a.url) } }
			s.certifications.each{ k, v -> certification { country(k); rating(v) } }
			s.crew.each{ p ->
				if (p.actor) {
					actor {
						name(p.name)
						if (p.character) { role(p.character) }
						if (Settings.ApplicationRevisionNumber > 10960) {
							if (p.order >= 0) { order(p.order) }
						}
						if (p.image) { thumb(p.image) }
					}
				} else if (p.director) {
					director(p.name)
				} else if (p.writer || p.department == 'Writing') {
					credits(p.name)
				}
			}
			if (s.database =~ /TheMovieDB/) { tmdb(id: s.id, 'https://www.themoviedb.org/tv/' + s.id) }
			if (s.database =~ /TheTVDB/) { tvdb(id: s.id, 'https://thetvdb.com/series/' + s.slug) }
			if (s.database =~ /AniDB/) { anidb(id: s.id, 'https://anidb.net/anime/' + s.id) }
			uniqueid(type: db, default: 'true', s.id)
		}
	}
	xml.saveAs(outputFile)
}

def getTVDBID(series, locale) {
	def sid = series.getExternalId(/TheTVDB/)
	if (sid) {
		return TheTVDB.getSeriesInfo(sid, locale)
	}
	return null
}

def fetchSeriesArtworkAndNfo(seriesDir, seasonDir, series, season, override, locale) {
	tryLogCatch {
		def details = series.details
		if (details == null) {
			log.finest "NFO not supported: $series"
			return
		}
		fetchSeriesNfo(seriesDir.resolve('tvshow.nfo'), details, locale)
		fetchPrimaryPoster(details.poster, seriesDir.resolve('folder.jpg'))

		def tvdbid = getTVDBID(series, locale)
		if (tvdbid == null) {
			log.finest "Artwork not supported: $series"
			return
		}

		fetchSeriesBanner(seriesDir.resolve('poster.jpg'), tvdbid, 'posters', 'series', null, override, locale)
		fetchSeriesBanner(seriesDir.resolve('banner.jpg'), tvdbid, 'banners', 'series', null, override, locale)
		fetchSeriesBanner(seriesDir.resolve('fanart.jpg'), tvdbid, 'backgrounds', 'series', null, override, locale)

		['hdclearart', 'clearart'].findResult{ type -> fetchSeriesFanart(seriesDir.resolve('clearart.png'), tvdbid, type, null, override, locale) }
		['hdtvlogo', 'clearlogo'].findResult{ type -> fetchSeriesFanart(seriesDir.resolve('logo.png'), tvdbid, type, null, override, locale) }
		fetchSeriesFanart(seriesDir.resolve('landscape.jpg'), tvdbid, 'tvthumb', null, override, locale)
	}
}

def fetchPrimaryPoster(url, file) {
	if (url && !file.exists()) {
		url.cache().saveAs(file)
	}
}

args.eachMediaFolder{ dir ->
	def videos = dir.listFiles{ it.isVideo() }
	def sxe = videos.findResult{ parseEpisodeNumber(it) }
	def locale = _args.language.locale ?: Locale.ENGLISH

	def query = _args.query
	if (!query) {
		def s = detectSeries(videos)
		if (s) {
			query = (s.getExternalId('TheTVDB') as String) ?: s.name
		}
	}
	if (!query) {
		query = dir.name
	}

	log.finest "$dir => Lookup by $query"
	def options = TheTVDB.lookup(query, locale)
	if (options.isEmpty()) {
		log.warning "TV Series not found: $query"
		return
	}
	options = options.sortBySimilarity(query){ it.name }
	def id = options.first()
	if (id == null) {
		return
	}

	def seriesInfo = TheTVDB.getSeriesInfo(id, locale)
	def season = sxe && sxe.season > 0 ? sxe.season : 1
	log.fine "$dir => $seriesInfo.name [$seriesInfo] season $season"

	// Series-wide extras (nfo, series poster/banner/fanart, clearart, logo).
	// seriesDir == seasonDir here, so the season-specific branch inside this
	// call never runs -- that's the bug we work around below.
	fetchSeriesArtworkAndNfo(dir, dir, seriesInfo, season, true, locale)

	def tvdbid = getTVDBID(seriesInfo, locale)
	if (tvdbid == null) {
		log.finest "$dir => artwork not supported for $seriesInfo"
		return
	}

	// Force season-specific art into this folder -- it IS the per-season
	// folder in MediaButler's flat layout, so poster.jpg must identify the
	// season, not just the show.
	def seasonPoster = fetchSeriesBanner(dir.resolve('poster.jpg'), tvdbid, 'posters', 'season', season, true, locale)
	if (seasonPoster == null) {
		println "[mediabutler] season-art-fallback: $dir (no season $season art on TheTVDB)"
	} else {
		fetchSeriesBanner(dir.resolve('folder.jpg'), tvdbid, 'posters', 'season', season, true, locale)
	}
	fetchSeriesBanner(dir.resolve('banner.jpg'), tvdbid, 'banners', 'season', season, true, locale)
	fetchSeriesFanart(dir.resolve('landscape.jpg'), tvdbid, 'seasonthumb', season, true, locale)
}
