Jellyscrub
====================
<img src="https://raw.githubusercontent.com/nicknsy/jellyscrub/main/logo/logo.png" width="500">

## About ##
Jellyscrub is a plugin that generates "trickplay" (Roku .bif) files that are then interpreted by the client and used for bufferless scrubbing image previews.

The trickplay data for a 1:30hr movie with 320x180 thumbnails only takes about 6MB of data when generating an image every 10 seconds. It also only takes a little over a minute to generate.

<b>Abilities</b>
* No buffering, even over proxies
* Customize interval between new images
* Generate trickplay files at multiple target resolutions
* Generate on library scan, as a scheduled task, or whenever they are requested by the client
* Option to save locally to media folder

<b>Limitations</b>
* Only works with web version of Jellyfin
* No options to limit libraries/media that have trickplay files generated

## Comparison ##

Jellyfin Default [<b>SSL, LOCAL</b>] (Minimum of 5m Interval):

Jellyscrub [<b>SSL, Cloudflare Proxy</b>] (Default 10s Interval, 320px width):

## Installation ##
1. Add https://github.com/nicknsy/jellyscrub/blob/main/manifest.json as a Jellyfin plugin repository
2. Install Jellyscrub from the repository
3. Restart the Jellyfin server
4. If you Jellyfin's web path is set, the plugin should automatically inject the companion client script into the "index.html" file of the web server directory. Otherwise, the line `<script plugin="Jellyscrub" version="1.0.0.0" src="/Trickplay/ClientScript"></script>` will have to be added at the end of end body tag manually right before `</body>` manually.
5. Clear your site cookies / local storage to get rid of the cached index file and receive a new one from the server.
6. Change any configuration options, like whether to save in media folders over internal metadata folders.
7. Run a scan (could take much longer depending on library size) or start watching a movie and the scrubbing preview should update in a few minutes.
