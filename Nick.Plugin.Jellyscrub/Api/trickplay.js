const MANIFEST_ENDPOINT = '/Trickplay/{itemId}/GetManifest';
const BIF_ENDPOINT = '/Trickplay/{itemId}/{width}/GetBIF';
const RETRY_INTERVAL = 60_000;  // ms (1 minute)

let mediaSourceId = null;
let mediaRuntimeTicks = null;   // NOT ms -- Microsoft DateTime.Ticks. Must be divided by 10,000.

const EMBY_AUTH_HEADER = 'X-Emby-Authorization';
let embyAuthValue = '';

let hasFailed = false;
let trickplayManifest = null;
let trickplayData = null;
let currentTrickplayFrame = null;

let hiddenSliderBubble = null;
let customSliderBubble = null;
let customThumbImg = null;
let customChapterText = null;

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
 * Inject style to be used for slider bubble popup
 */

let jellyscrubStyle = document.createElement('style');
jellyscrubStyle.textContent = '.jellyscrubThumbText {margin: 0;}';
document.body.insertBefore(jellyscrubStyle, document.body.firstChild);

/*
 * Code for updating and locking mediaSourceId and getBubbleHtml 
 */

// Grab MediaSourceId from jellyfin-web internal API calls
const { fetch: originalFetch } = window;

window.fetch = async (...args) => {
    let [resource, config] = args;

    let url = new URL(resource);
    let isPlaybackInfo = url.pathname.split('/').pop() == 'PlaybackInfo';

    if (isPlaybackInfo) {
        // Clear old values
        clearTimeout(mainScriptExecution);

        mediaSourceId = null;
        mediaRuntimeTicks = null;
    
        embyAuthValue = '';
    
        hasFailed = false;
        trickplayManifest = null;
        trickplayData = null;
        currentTrickplayFrame = null;

        hiddenSliderBubble = null;
        customSliderBubble = null;
        customThumbImg = null;
        customChapterText = null;
    
        osdPositionSlider = null;
        osdGetBubbleHtml = null;
        osdGetBubbleHtmlLock = false;
        // Clear old values

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
function containerCallback(mutationList, observer) {
    for (const mutation of mutationList) {
        if (mutation.target.classList.contains('mdl-slider-container')) {
            debug(`Found OSD container: ${mutation.target}`);

            let slider = mutation.target.getElementsByClassName('osdPositionSlider')[0];
            if (slider) {
                osdPositionSlider = slider;
                debug(`Found OSD slider: ${osdPositionSlider}`);

                Object.defineProperty(osdPositionSlider, 'getBubbleHtml', {
                    get() { return osdGetBubbleHtml },
                    set(value) { if (!osdGetBubbleHtmlLock) osdGetBubbleHtml = value; },
                    configurable: true,
                    enumerable: true
                });

                let bubble = mutation.target.getElementsByClassName('sliderBubble')[0];
                if (bubble) {
                    hiddenSliderBubble = bubble;
                    //hiddenSliderBubble.classList.add('jellyscrub-hide');
    
                    let customBubble = document.createElement('div');
                    customBubble.classList.add('sliderBubble', 'jellyscrub-slider', 'hide');
    
                    let customThumbContainer = document.createElement('div');
                    customThumbContainer.classList.add('chapterThumbContainer');
    
                    customThumbImg = document.createElement('img');
                    customThumbImg.classList.add('chapterThumb');
                    customThumbImg.src = 'data:,';
                    customThumbContainer.appendChild(customThumbImg);

                    let customChapterTextContainer = document.createElement('div');
                    customChapterTextContainer.classList.add('chapterThumbTextContainer');

                    customChapterText = document.createElement('h2');
                    customChapterText.classList.add('jellyscrubThumbText');
                    customChapterText.textContent = '--:--';
                    customChapterTextContainer.appendChild(customChapterText);
    
                    customThumbContainer.appendChild(customChapterTextContainer);
                    customBubble.appendChild(customThumbContainer);
                    customSliderBubble = hiddenSliderBubble.parentElement.appendChild(customBubble);

                    let sliderConfig = { attributeFilter: ['style', 'class'] };
                    let sliderObserver = new MutationObserver(sliderCallback);
                    sliderObserver.observe(hiddenSliderBubble, sliderConfig); 
                }
    
                // Don't know if this will be triggered by the fetch intercept first or the observer
                // Don't run main script if there is already trickplay data
                if (!hasFailed && !trickplayData && mediaSourceId && mediaRuntimeTicks && embyAuthValue
                    && osdPositionSlider && hiddenSliderBubble && customSliderBubble) mainScriptExecution();
            }
        }
    }
};

function sliderCallback(mutationList, observer) {
    for (const mutation of mutationList) {
        switch(mutation.attributeName) {
            case 'style':
                customSliderBubble.setAttribute('style', mutation.target.getAttribute('style'));
                break;
            case 'class':
                if (mutation.target.classList.contains('hide')) {
                    customSliderBubble.classList.add('hide');
                } else if (trickplayData) {
                    customSliderBubble.classList.remove('hide');
                }
                break;
        }
    }
}

let targetObserveNode = document.getElementsByClassName('mainAnimatedPages')[0];
let containerConfig = { childList: true, subtree: true };
let containerObserver = new MutationObserver(containerCallback);
containerObserver.observe(targetObserveNode, containerConfig);

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

const BIF_MAGIC_NUMBERS = [0x89, 0x42, 0x49, 0x46, 0x0D, 0x0A, 0x1A, 0x0A];
const SUPPORTED_BIF_VERSION = 0;

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
        setTimeout(mainScriptExecution, 0); // Hacky way of avoiding using fetch/await by returning then calling function again
    } else if (this.status == 503) {
        info(`Received 503 from server -- still generating manifest. Waiting ${RETRY_INTERVAL}ms then retrying...`);
        setTimeout(mainScriptExecution, RETRY_INTERVAL);
    } else {
        debug(`Failed to get manifest file: url ${this.responseURL}, error ${this.status}, ${this.responseText}`)
        hasFailed = true;
    }
}

function bifLoad() {
    if (this.status == 200) {
        trickplayData = trickplayDecode(this.response);
        setTimeout(mainScriptExecution, 0); // Hacky way of avoiding using fetch/await by returning then calling function again
    } else if (this.status == 503) {
        info(`Received 503 from server -- still generating BIF. Waiting ${RETRY_INTERVAL}ms then retrying...`);
        setTimeout(mainScriptExecution, RETRY_INTERVAL);
    } else {
        if (this.status == 404) error('Requested BIF file listed in manifest but server returned 404 not found.');

        debug(`Failed to get BIF file: url ${this.responseURL}, error ${this.status}, ${this.responseText}`)
        hasFailed = true;
    }
}

function mainScriptExecution() {
    // Get trickplay manifest file
    if (!trickplayManifest) {
        let manifestUrl = MANIFEST_ENDPOINT.replace('{itemId}', mediaSourceId);
        let manifestRequest = new XMLHttpRequest();
        manifestRequest.responseType = 'json';
        manifestRequest.addEventListener('load', manifestLoad);

        manifestRequest.open('GET', manifestUrl);
        manifestRequest.setRequestHeader(EMBY_AUTH_HEADER, embyAuthValue);

        debug(`Requesting Manifest @ ${manifestUrl}`);
        manifestRequest.send();
        return;
    }

    // Get trickplay BIF file
    if (!trickplayData && trickplayManifest) {
        // Determine which width to use
        // Prefer highest resolution @ less than 20% of total screen resolution width
        let resolutions = trickplayManifest.WidthResolutions;

        if (resolutions && resolutions.length > 0)
        {
            resolutions.sort();
            let screenWidth = window.screen.width * window.devicePixelRatio;
            let width = resolutions[0];

            // Prefer bigger trickplay images granted they are less than or equal to 20% of total screen width
            for (let i = 1; i < resolutions.length; i++)
            {
                let biggerWidth = resolutions[i];
                if (biggerWidth <= (screenWidth * .2)) width = biggerWidth;
            }
            info(`Requesting BIF file with width ${width}`);

            let bifUrl = BIF_ENDPOINT.replace('{itemId}', mediaSourceId).replace('{width}', width);
            let bifRequest = new XMLHttpRequest();
            bifRequest.responseType = 'arraybuffer';
            bifRequest.addEventListener('load', bifLoad);

            bifRequest.open('GET', bifUrl);
            bifRequest.setRequestHeader(EMBY_AUTH_HEADER, embyAuthValue);

            debug(`Requesting BIF @ ${bifUrl}`);
            bifRequest.send();
            return;
        } else {
            error(`Have manifest file with no listed resolutions: ${trickplayManifest}`);
        }
    }

    // Set the bubble function to our custom trickplay one
    if (trickplayData) {
        osdPositionSlider.getBubbleHtml = getBubbleHtmlTrickplay;
        osdGetBubbleHtmlLock = true;
    }
}

function getBubbleHtmlTrickplay(sliderValue) {
    //showOsd();

    let currentTicks = mediaRuntimeTicks * (sliderValue / 100);
    let currentTimeMs = currentTicks / 10_000
    let imageSrc = getTrickplayFrameUrl(currentTimeMs, trickplayData);

    if (imageSrc) {
        if (currentTrickplayFrame) URL.revokeObjectURL(currentTrickplayFrame);
        currentTrickplayFrame = imageSrc;

        customThumbImg.src = imageSrc;
        customChapterText.textContent = getDisplayRunningTime(currentTicks);
    }

    return `<div style="min-width: ${customSliderBubble.offsetWidth}px; max-height: 0px"></div>`;
}

// Not the same, but should be functionally equaivalent to --
// https://github.com/jellyfin/jellyfin-web/blob/8ff9d63e25b40575e02fe638491259c480c89ba5/src/controllers/playback/video/index.js#L237
/*
function showOsd() {
    //document.getElementsByClassName('skinHeader')[0]?.classList.remove('osdHeader-hidden');
    // todo: actually can't be bothered so I'll wait and see if it works without it or not
}
*/

// Taken from https://github.com/jellyfin/jellyfin-web/blob/8ff9d63e25b40575e02fe638491259c480c89ba5/src/scripts/datetime.js#L76
function getDisplayRunningTime(ticks) {
    const ticksPerHour = 36000000000;
    const ticksPerMinute = 600000000;
    const ticksPerSecond = 10000000;

    const parts = [];

    let hours = ticks / ticksPerHour;
    hours = Math.floor(hours);

    if (hours) {
        parts.push(hours);
    }

    ticks -= (hours * ticksPerHour);

    let minutes = ticks / ticksPerMinute;
    minutes = Math.floor(minutes);

    ticks -= (minutes * ticksPerMinute);

    if (minutes < 10 && hours) {
        minutes = '0' + minutes;
    }
    parts.push(minutes);

    let seconds = ticks / ticksPerSecond;
    seconds = Math.floor(seconds);

    if (seconds < 10) {
        seconds = '0' + seconds;
    }
    parts.push(seconds);

    return parts.join(':');
}