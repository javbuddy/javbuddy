// Lets go of a player's <video> once it leaves the page: when another movie or version gets a fresh
// one, or the player closes. Taking a <video> out of the page only pauses it; it keeps its stream's
// HTTP request open, and its buffered media, for as long as it isn't garbage-collected. The browser
// allows only 6 connections to a server over HTTP/1.1, so after a few movies of a playlist every new
// stream, image and trickplay sheet queued behind the old videos' streams. Clearing the src and calling
// load() aborts the request and drops the media.
//
// One observer for the whole page, started by the first player and kept: it sees every removal however
// it happens (Next, N, a version switch, closing, Review mode's next movie), however quickly renders
// follow each other, without a round trip per video.

import { releaseVideo } from './ScrubBar.razor.js';

const PLAYER_VIDEO = 'video.video-player-media';

let observer = null;

export function watch() {
    if (observer) return;
    observer = new MutationObserver((mutations) => {
        for (const mutation of mutations) {
            for (const node of mutation.removedNodes) {
                if (node.nodeType !== Node.ELEMENT_NODE) continue;
                if (node.matches(PLAYER_VIDEO)) release(node);
                else for (const video of node.getElementsByTagName('video')) {
                    if (video.matches(PLAYER_VIDEO)) release(video);
                }
            }
        }
    });
    observer.observe(document.body, { childList: true, subtree: true });
}

function release(video) {
    // Back in the page by now means it was moved rather than removed.
    if (video.isConnected || !video.hasAttribute('src')) return;
    releaseVideo(video);
}
