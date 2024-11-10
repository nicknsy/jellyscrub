Jellyscrub
====================
<img src="https://raw.githubusercontent.com/nicknsy/jellyscrub/main/logo/logo.png" width="500">

## ⚠️ Warning for Jellyfin 10.9+ ⚠️ ##
Trickplay functionality has been upstreamed into Jellyfin 10.9.0, and as such, <b>Jellyscrub's trickplay functionality will not be maintained after 10.9.0 is officially released.</b> However, Jellyscrub has been updated to version 2.0.0 which only allows for the conversion of your already generated .bif files to Jellyfin's new native format.

<b>All that is required to convert your old .bif files is to update the plugin to the latest version through Jellyfin, restart the server, and visit the plugin configuration page in the dashboard.</b>

## About ##
Jellyscrub is a plugin that generates "trickplay" (Roku .bif) files that are then interpreted by the client and used for bufferless scrubbing image previews.

The trickplay data for a 1:30hr movie with 320x180 thumbnails only takes about 6MB of data when generating an image every 10 seconds. Takes around one - four minutes to generate depending on computer hardware.

<b>Abilities</b>
* No buffering, even over proxies
* Works with web, desktop, Android phone, iOS client -- see last installation instruction to get working on desktop
* Customize interval between new images
* Generate trickplay files at multiple target resolutions
* Generate on library scan, as a scheduled task, or whenever they are requested by the client
* Option to save locally to media folder

<b>Limitations</b>
* No options to limit libraries/media that have trickplay files generated

## Comparison ##

Jellyfin Default [<b>SSL, Cloudflare Proxy</b>] (Minimum of 5m Interval):
<br/>
<img src="https://github.com/nicknsy/jellyscrub/raw/main/logo/jellyfin-cloudflare.gif" width="500">

Jellyscrub [<b>SSL, Cloudflare Proxy</b>] (Default 10s Interval, 320px width):
<br/>
<img src="https://github.com/nicknsy/jellyscrub/raw/main/logo/jellyscrub-cloudflare.gif" width="500">

Jellyscrub on iOS [<b>Single Screenshot, Functions Same as Above</b>]:
<br/>
<img src="https://github.com/nicknsy/jellyscrub/raw/main/logo/jellyscrub-ios.jpg" width="500">

## Installation ##
<b>NOTE: The client script will fail to inject automatically into the jellyfin-web server if there is a difference in permission between the owner of the web files (root, or www-data, etc.) and the executor of the main jellyfin-server. This often happens because...</b>
* <b>Docker -</b> the container is being run as a non-root user while having been built as a root user, causing the web files to be owned by root. To solve this, you can remove any lines like `User: 1000:1000`, `GUID:`, `PID:`, etc. from the jellyfin docker compose file.
* <b>Install from distro repositories -</b> the jellyfin-server will execute as the `jellyfin` user while the web files will be owned by `root`, `www-data`, etc. This can <i>likely</i> be fixed by adding the `jellyfin` (or whichever user your main jellyfin server runs as) user to the same group the jellyfin-web folders are owned by. You should only do this if they are owned by a group other than root, and will have to lookup how to manage permissions on your specific distro.
* <b>Alternatively, the script can manually be added to the index.html as described below.</b>

<b>NOTE: If you manually injected the script tag, you will have to manually inject it on every jellyfin-web update, as the index.html file will get overwritten. However, for normal Jellyscrub updates the script tag will not need to be changed as the plugin will return the latest script from /ClientScript</b>

1. Add https://raw.githubusercontent.com/nicknsy/jellyscrub/main/manifest.json as a Jellyfin plugin repository
2. Install Jellyscrub from the repository
3. Restart the Jellyfin server
4. If your Jellyfin's web path is set, the plugin should automatically inject the companion client script into the "index.html" file of the web server directory. Otherwise, the line `<script plugin="Jellyscrub" version="1.0.0.0" src="/Trickplay/ClientScript"></script>` will have to be added at the end of the body tag manually right before `</body>`. If you have a base path set, change `src="/Trickplay/ClientScript"` to `src="/YOUR_BASE_PATH/Trickplay/ClientScript"`.
5. Clear your site cookies / local storage to get rid of the cached index file and receive a new one from the server.
6. Change any configuration options, like whether to save in media folders over internal metadata folders.
7. Run a scan (could take much longer depending on library size) or start watching a movie and the scrubbing preview should update in a few minutes.
8. OPTIONAL: In the JMP desktop client (version >= 1.8.1), click on your profile image, go to "Client Settings", and tick "Jellyscrub" under plugin support. Restart for changes to take effect.
