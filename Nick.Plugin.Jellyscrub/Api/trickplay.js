const MANIFEST_ENDPOINT = '/Trickplay/{itemId}/GetManifest';
const BIF_ENDPOINT = '/Trickplay/{itemId}/{width}/GetBIF';
const RETRY_INTERVAL = 60_000;  // ms (1 minute)

let mediaSourceId = null;
let mediaRuntimeTicks = null;   // NOT ms. Must be divided by 10,000.

const EMBY_AUTH_HEADER = 'X-Emby-Authorization';
let embyAuthValue = '';

let trickplayManifest = null;
let trickplayData = null;
let currentTrickplayFrame = null;

let osdPositionSlider = null;
let osdGetBubbleHtml = null;
let osdGetBubbleHtmlLock = false;

/*
 * Utility methods
 */

const LOG_PREFIX  = '[jellyscrub] ';

function debug(msg) {
    console.debug(LOG_PREFIX + msg);
}

function error(msg) {
    console.error(LOG_PREFIX + msg);
}

function info(msg) {
    console.info(LOG_PREFIX + msg);
}

/*
 * Code for updating and locking mediaSourceId and getBubbleHtml 
 */

// On page change these variables must manually be set back to null as new pages are simply pushState's and
// wont reload the script.
addEventListener('popstate', (event) => {
    clearTimeout(mainScriptExecution);

    embyAuthValue = '';

    mediaSourceId = null;
    mediaRuntimeTicks = null;

    trickplayManifest = null;
    trickplayData = null;
    currentTrickplayFrame = null;

    osdPositionSlider = null;
    osdGetBubbleHtml = null;
    osdGetBubbleHtmlLock = false;
}); 

// Grab MediaSourceId from jellyfin-web internal API calls
const { fetch: originalFetch } = window;

window.fetch = async (...args) => {
    let [resource, config] = args;

    let url = new URL(resource);
    let isPlaybackInfo = url.pathname.split('/').pop() == 'PlaybackInfo';

    if (isPlaybackInfo) {
        mediaSourceId = new URLSearchParams(url.search).get('MediaSourceId');
        debug(`Found media source ID: ${mediaSourceId}`);

        let auth = config.headers['X-Emby-Authorization'];
        embyAuthValue = auth ? auth : '';
        debug(`Using Emby auth value: ${embyAuthValue}`);
    }

    const response = await originalFetch(resource, config);

    if (isPlaybackInfo) {
        response.clone().json().then((data) => {
            for (const source of data.MediaSources) {
                if (source.Id == mediaSourceId) {
                    mediaRuntimeTicks = source.RunTimeTicks;
                    debug(`Found media runtime of ${mediaRuntimeTicks} ticks`);
                    break;
                }
            }
        });
    }

    return response;
};

// Observe when video player slider is added to know when playback starts
// and to set/lock getBubbleHtml function
const targetNode = document.getElementsByClassName('mainAnimatedPages')[0];
const config = { childList: true, subtree: true };

const callback = function (mutationList, observer) {
    for (const mutation of mutationList) {
        if (mutation.target.classList.contains('mdl-slider-container')) {
            debug(`Found OSD container: ${mutation.target}`);
            osdPositionSlider = mutation.target.getElementsByClassName('osdPositionSlider')[0];
            if (osdPositionSlider) {
                debug(`Found OSD slider: ${osdPositionSlider}`);

                Object.defineProperty(osdPositionSlider, 'getBubbleHtml', {
                    get() { return osdGetBubbleHtml },
                    set(value) { if (!osdGetBubbleHtmlLock) osdGetBubbleHtml = value; },
                    configurable: true,
                    enumerable: true
                });
            }
        }
    }
};

const observer = new MutationObserver(callback);
observer.observe(targetNode, config);

/*
 * Indexed UInt8Array
 */

function Indexed8Array(buffer) {
    this.index = 0;
    this.array = new Uint8Array(buffer);
}

Indexed8Array.prototype.read = function(len) {
    if (len) {
        readData = [];
        for (let i = 0; i < len; i++) {
            readData.push(this.array[this.index++]);
        }

        return readData;
    } else {
        return this.array[this.index++];
    }
}

Indexed8Array.prototype.readArbitraryInt = function(len) {
    let num = 0;
    for (let i = 0; i < len; i++) {
        num += this.read() << (i << 3);
    }

    return num;
}

Indexed8Array.prototype.readInt32 = function() {
    return this.readArbitraryInt(4);
}

/*
 * Code for BIF/Trickplay frames
 */

function trickplayDecode(buffer) {
    info(`BIF file size: ${(buffer.byteLength / 1_048_576).toFixed(2)}MB`);


    let bifArray = new Indexed8Array(buffer);
    for (let i = 0; i < BIF_MAGIC_NUMBERS.length; i++) {
        if (bifArray.read() != BIF_MAGIC_NUMBERS[i]) {
            error('Attempted to read invalid bif file.');
            error(buffer);
            return null;
        }
    }

    let bifVersion = bifArray.readInt32();
    if (bifVersion != SUPPORTED_BIF_VERSION) {
        error(`Client only supports BIF v${SUPPORTED_BIF_VERSION} but file is v${bifVersion}`);
        return null;
    }

    let bifImgCount = bifArray.readInt32();
    info(`BIF image count: ${bifImgCount}`);

    let timestampMultiplier = bifArray.readInt32();
    if (timestampMultiplier == 0) timestampMultiplier = 1000;

     bifArray.read(44); // Reserved

    let bifIndex = [];
    for (let i = 0; i < bifImgCount; i++) {
        bifIndex.push({
            timestamp: bifArray.readInt32(),
            offset: bifArray.readInt32()
        });
    }

    let bifImages = [];
    for (let i = 0; i < bifIndex.length; i++) {
        indexEntry = bifIndex[i];
        timestamp = indexEntry.timestamp;
        offset = indexEntry.offset;
        nextOffset = bifIndex[i + 1] ? bifIndex[i + 1].offset : buffer.length;

        bifImages[timestamp] = buffer.slice(offset, nextOffset);
    }
    
    return {
        version: bifVersion,
        timestampMultiplier: timestampMultiplier,
        imageCount: bifImgCount,
        images: bifImages
    };
}

function getTrickplayFrame(playerTimestamp, data) {
    multiplier = data.timestampMultiplier;
    images = data.images;

    frame = Math.floor(playerTimestamp / multiplier);
    return images[frame];
}

function getTrickplayFrameUrl(playerTimestamp, data) {
    let bufferImage = getTrickplayFrame(playerTimestamp, data);

    if (bufferImage) {
        return URL.createObjectURL(new Blob([bufferImage], {type: 'image/jpeg'}));
    }
}

/*
 * Main script execution -- not actually run first
 */

function manifestLoad() {
    if (this.status == 200) {
        trickplayManifest = this.response;
    } else if (this.status == 503) {
        info(`Received 503 from server -- still generating manifest. Waiting ${RETRY_INTERVAL}ms then retrying...`);
        setTimeout(mainScriptExecution, RETRY_INTERVAL);
    }
}

function bifLoad() {
    if (this.status == 200) {
        trickplayData = trickplayDecode(this.response);
    } else if (this.status == 503) {
        info(`Received 503 from server -- still generating BIF. Waiting ${RETRY_INTERVAL}ms then retrying...`);
        setTimeout(mainScriptExecution, RETRY_INTERVAL);
    } else if (this.status == 404) {
        error('Requested BIF file listed in manifest but server returned 404 not found.');
    }
}

function mainScriptExecution() {
    // Get trickplay manifest file
    if (!trickplayManifest) {
        let manifestRequest = new XMLHttpRequest();
        manifestRequest.setRequestHeader(EMBY_AUTH_HEADER, embyAuthValue);
        manifestRequest.responseType = 'json';
        manifestRequest.addEventListener('load', manifestLoad);

        manifestRequest.open('GET', MANIFEST_ENDPOINT.replace('{itemId}', mediaSourceId));
        manifestRequest.send();
    }

    // Get trickplay BIF file
    if (!trickplayData && trickplayManifest) {
        // Determine which width to use
        // Prefer highest resolution @ less than 20% of total screen resolution width
        let width = null;
        ///

        if (width)
        {
            info(`Requesting BIF file with width ${width}`);

            let bifRequest = new XMLHttpRequest();
            bifRequest.setRequestHeader(EMBY_AUTH_HEADER, embyAuthValue);
            bifRequest.responseType = 'arraybuffer';
            bifRequest.addEventListener('load', bifLoad);

            bifRequest.open('GET', BIF_ENDPOINT.replace('{itemId}', mediaSourceId).replace('{width}', width));
            bifRequest.send();
        }
    }

    // Set the bubble function to our custom trickplay one
    if (trickplayData && osdPositionSlider) {
        osdPositionSlider.getBubbleHtml = getBubbleHtmlTrickplay;
        osdGetBubbleHtmlLock = true;
    }
}

function getBubbleHtmlTrickplay(sliderValue) {
    showOsd();

    let currentTicks = mediaRuntimeTicks * (sliderValue / 100) / 10_000;
    let imageSrc = getTrickplayFrameUrl(currentTicks, trickplayData);

    if (imageSrc) {
        if (currentTrickplayFrame) URL.revokeObjectURL(currentTrickplayFrame);
        currentTrickplayFrame = imageSrc;

        let html = '<div class="chapterThumbContainer">';
        html += '<img class="chapterThumb" src="' + imageSrc + '" />';
        html += '<div class="chapterThumbTextContainer">';
        //html += '<div class="chapterThumbText chapterThumbText-dim">';
        //html += escapeHtml(chapter.Name);
        //html += '</div>';
        html += '<h2 class="chapterThumbText">';
        html += datetime.getDisplayRunningTime(positionTicks);
        html += '</h2>';
        html += '</div>';

        return html + '</div>';
    }

    return null;
}