Jellyscrub
====================
<img src="https://raw.githubusercontent.com/nicknsy/jellyscrub/main/logo/logo.png" width="500">

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
* <b>Install from distro repositories -</b> the jellyfin-server will execute as `jellyfin` user while the web files will be owned by `root`, `www-data`, etc. This can <i>likely</i> be fixed by adding the `jellyfin` (or whichecher user your main jellyfin server runs at) to the same group the jellyfin-web folders are owned by. You should only do this if they are owned by a group other than root, and will have to lookup how to manage permissions on your specific distro.
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

<b>NOTE: If you're using the [LinuxServer.io Jellyfin Docker Image](https://docs.linuxserver.io/images/docker-jellyfin/) and a custom UID/GID, you can leverage their [custom script framework](https://docs.linuxserver.io/general/container-customization/) to inject your own user script which injects the Client Script into index.html upon initialization of the container.</b>

1. Create a folder containing a bash script which injects the Client Script into index.html.

    Example `custom_scripts/jellyscrub_injection.sh` script:
```bash
#!/bin/bash

if grep -q "Jellyscrub" /usr/share/jellyfin/web/index.html; then
    echo "Content already exists. No changes needed."
else
    sed -i 's|</body|<script plugin="Jellyscrub" version="1.0.0.0" src="/Trickplay/ClientScript"></script></body|' /usr/share/jellyfin/web/index.html && echo "Content inserted successfully."
fi
```
2. Modify your Docker configuration to include a volume mount to a directory containing the script.

    Example `docker-compose.yml`:
```docker
version: "3.7"
services:

  jellyfin:
    image: lscr.io/linuxserver/jellyfin:latest
    container_name: jellyfin
    environment:
        PUID: "99"
        PGID: "100"
    volumes:
      - ./custom_scripts:/custom-cont-init.d:ro # custom startup script for Jellyscrub plugin client script injection
    restart: always
```
