Jellyscrub
====================
<img src="https://raw.githubusercontent.com/nicknsy/jellyscrub/main/logo/logo.png" width="500">

## About ##
Jellyscrub is a plugin that generates "trickplay" (Roku .bif) files that are then interpreted by the client and used for bufferless scrubbing image previews.

The trickplay data for a 1:30hr movie with 320x180 thumbnails only takes about 6MB of data when generating an image every 10 seconds. Takes around one - four minutes to generate depending on computer hardware.

<b>Abilities</b>
* No buffering, even over proxies
* <s>Customize interval between new images</s> Don't use this for now, will be fixed tomorrow with 1.0.1
* Generate trickplay files at multiple target resolutions
* Generate on library scan, as a scheduled task, or whenever they are requested by the client
* Option to save locally to media folder

<b>Limitations</b>
* Only works with web version of Jellyfin
* No options to limit libraries/media that have trickplay files generated

## Comparison ##

Jellyfin Default [<b>SSL, Cloudflare Proxy</b>] (Minimum of 5m Interval):
<br/>
<img src="https://github.com/nicknsy/jellyscrub/raw/main/logo/jellyfin-cloudflare.gif" width="500">

Jellyscrub [<b>SSL, Cloudflare Proxy</b>] (Default 10s Interval, 320px width):
<br/>
<img src="https://github.com/nicknsy/jellyscrub/raw/main/logo/jellyscrub-cloudflare.gif" width="500">

## Installation ##
<b>NOTE: If the script is unable to inject due to a lack of permission, this is likely due to the docker container being run as a non-root user while having been built as a root user, causing the web files to be owned by root. To solve this, you can remove any lines like `User: 1000:1000`, `GUID:`, `PID:`, etc. from the jellyfin docker compose file. Alternatively, the script can manually be added to the index as described below.</b>

1. Add https://raw.githubusercontent.com/nicknsy/jellyscrub/main/manifest.json as a Jellyfin plugin repository
2. Install Jellyscrub from the repository
3. Restart the Jellyfin server
4. If your Jellyfin's web path is set, the plugin should automatically inject the companion client script into the "index.html" file of the web server directory. Otherwise, the line `<script plugin="Jellyscrub" version="1.0.0.0" src="/Trickplay/ClientScript"></script>` will have to be added at the end of the body tag manually right before `</body>`.
5. Clear your site cookies / local storage to get rid of the cached index file and receive a new one from the server.
6. Change any configuration options, like whether to save in media folders over internal metadata folders.
7. Run a scan (could take much longer depending on library size) or start watching a movie and the scrubbing preview should update in a few minutes.
