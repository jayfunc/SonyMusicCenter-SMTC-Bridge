const electron = require('electron');
const cp = require('child_process');
const path = require('path');
const http = require('http');

let smtcProc;
try {
    const bridgePath = path.join(process.resourcesPath, '..', 'SonyMusicCenterSMTC.exe');
    smtcProc = cp.spawn(bridgePath, [], { detached: false, windowsHide: true });
} catch (e) {}

electron.app.on('will-quit', () => {
    if (smtcProc) {
        try { smtcProc.kill(); } catch (e) {}
    }
});

electron.app.on('web-contents-created', (e, wc) => {
    wc.on('console-message', (e, level, msg) => {
        if (msg && msg.startsWith('SMTC_UPDATE|')) {
            try {
                const payloadStr = msg.substring(12);
                const byteLen = Buffer.byteLength(payloadStr, 'utf8');
                const req = http.request({
                    hostname: '127.0.0.1',
                    port: 9999,
                    path: '/update',
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json; charset=utf-8',
                        'Content-Length': byteLen
                    }
                });
                req.on('error', () => {});
                req.write(payloadStr, 'utf8');
                req.end();
            } catch (e) {}
        }
    });

    const onReady = () => {
        const url = wc.getURL();
        if (!url.includes('window-main')) return;

        if (!wc.__smtcPollStarted) {
            wc.__smtcPollStarted = true;
            const timer = setInterval(() => {
                if (wc.isDestroyed()) {
                    clearInterval(timer);
                    return;
                }
                http.get('http://127.0.0.1:9999/poll', res => {
                    let raw = '';
                    res.on('data', chunk => raw += chunk);
                    res.on('end', () => {
                        const data = (raw || '').trim().toLowerCase();
                        if (!data) return;

                        if (data.startsWith('seek:')) {
                            const targetMs = parseInt(data.substring(5), 10);
                            if (isFinite(targetMs) && targetMs >= 0) {
                                const seekCode = `
                                    (function() {
                                        var targetMs = ${targetMs};
                                        var slider = document.getElementById('player-position-slider');
                                        if (!slider) return;
                                        var maxMs = parseFloat(slider.max);
                                        if (isFinite(maxMs) && maxMs > 0) {
                                            targetMs = Math.max(0, Math.min(targetMs, maxMs));
                                        }

                                        var inputEl = (slider.tagName === 'INPUT')
                                            ? slider
                                            : (slider.querySelector ? slider.querySelector('input[type="range"]') : null);
                                        if (!inputEl && slider.shadowRoot) {
                                            inputEl = slider.shadowRoot.querySelector('input[type="range"]');
                                        }

                                        var targetEl = inputEl || slider;
                                        var desc = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value');
                                        if (targetEl.tagName === 'INPUT' && desc && desc.set) {
                                            desc.set.call(targetEl, String(targetMs));
                                        } else {
                                            targetEl.value = targetMs;
                                        }
                                        ['input', 'change', 'value-changed'].forEach(function(evtName) {
                                            targetEl.dispatchEvent(new Event(evtName, { bubbles: true, composed: true }));
                                        });

                                        setTimeout(function() {
                                            var cur = parseFloat(slider.value);
                                            if (isFinite(cur) && Math.abs(cur - targetMs) > 2500 && isFinite(maxMs) && maxMs > 0) {
                                                var rect = slider.getBoundingClientRect();
                                                if (rect && rect.width > 0 && rect.height > 0) {
                                                    var ratio = Math.max(0, Math.min(1, targetMs / maxMs));
                                                    var cx = rect.left + rect.width * ratio;
                                                    var cy = rect.top + rect.height / 2;
                                                    ['pointerdown', 'mousedown', 'pointerup', 'mouseup', 'click'].forEach(function(type) {
                                                        var Evt = (type.startsWith('pointer') && typeof PointerEvent !== 'undefined') ? PointerEvent : MouseEvent;
                                                        slider.dispatchEvent(new Evt(type, {
                                                            bubbles: true,
                                                            cancelable: true,
                                                            composed: true,
                                                            view: window,
                                                            clientX: cx,
                                                            clientY: cy,
                                                            button: 0,
                                                            buttons: (type === 'pointerdown' || type === 'mousedown') ? 1 : 0
                                                        }));
                                                    });
                                                }
                                            }
                                        }, 600);
                                    })();
                                `;
                                wc.executeJavaScript(seekCode, true).catch(() => {});
                            }
                            return;
                        }

                        let targetId = '';
                        if (data === 'play' || data === 'pause' || data === 'playpause') {
                            targetId = 'player-control-play-pause';
                        } else if (data === 'next') {
                            targetId = 'player-control-next';
                        } else if (data === 'prev' || data === 'previous') {
                            targetId = 'player-control-previous';
                        }

                        if (targetId) {
                            const code = `
                                (function() {
                                    var b = document.getElementById('${targetId}');
                                    if (!b) return;
                                    ['pointerdown', 'mousedown', 'pointerup', 'mouseup', 'click'].forEach(function(type) {
                                        var Evt = (type.startsWith('pointer') && typeof PointerEvent !== 'undefined') ? PointerEvent : MouseEvent;
                                        b.dispatchEvent(new Evt(type, { bubbles: true, cancelable: true, view: window }));
                                    });
                                })();
                            `;
                            wc.executeJavaScript(code, true).catch(() => {});
                        }
                    });
                }).on('error', () => {});
            }, 300);

            wc.on('destroyed', () => clearInterval(timer));
        }

        wc.executeJavaScript(`
            (function() {
                if (window.__smtcBridgeHookLoaded) return;
                window.__smtcBridgeHookLoaded = true;

                let lastTitle = '';
                let lastState = '';
                let lastPosition = 0;
                let lastDuration = 0;
                let lastUpdateTime = Date.now();
                let lastCoverUrl = '';
                let lastCoverData = '';

                function getText(dataType) {
                    let text = '';
                    const els = document.querySelectorAll('.player-info-text[data-type="' + dataType + '"]');
                    els.forEach(el => { if (el.textContent.trim()) text = el.textContent.trim(); });
                    return text;
                }

                function getBase64Image(imgEl) {
                    try {
                        const canvas = document.createElement('canvas');
                        canvas.width = imgEl.naturalWidth || imgEl.width || 300;
                        canvas.height = imgEl.naturalHeight || imgEl.height || 300;
                        const ctx = canvas.getContext('2d');
                        ctx.drawImage(imgEl, 0, 0);
                        return canvas.toDataURL('image/jpeg', 0.9);
                    } catch (e) {
                        return '';
                    }
                }

                setInterval(() => {
                    const playBtn = document.getElementById('player-control-play-pause');
                    if (!playBtn) return;

                    const isPlaying = playBtn.getAttribute('data-icon') === 'pause';
                    const title = getText('title') || '';
                    const artist = getText('artist') || '';
                    const album = getText('album-title') || '';

                    let position = '0';
                    let duration = '0';
                    const slider = document.getElementById('player-position-slider');
                    if (slider) {
                        const posMs = parseFloat(slider.value);
                        const durMs = parseFloat(slider.max);
                        position = (isFinite(posMs) ? posMs / 1000 : 0).toString();
                        duration = (isFinite(durMs) ? durMs / 1000 : 0).toString();
                    }

                    let coverUrl = '';
                    let coverImgEl = null;
                    const pickCoverIn = (root) => {
                        let best = null;
                        let bestArea = 0;
                        const cands = root.querySelectorAll('img, div');
                        cands.forEach(el => {
                            let u = '';
                            if (el.tagName && el.tagName.toLowerCase() === 'img') {
                                u = el.src;
                            } else {
                                const bg = window.getComputedStyle(el).backgroundImage;
                                if (bg && bg !== 'none' && bg.includes('url(')) {
                                    const match = bg.match(/url\\(["']?(.*?)["']?\\)/);
                                    if (match && match[1]) u = match[1];
                                }
                            }
                            if (!u || u.includes('.svg') || u.includes('icon') || u.includes('default')) return;
                            const r = el.getBoundingClientRect();
                            if (r.width < 24 || r.height < 24) return;
                            const area = r.width * r.height;
                            if (area > bestArea) {
                                bestArea = area;
                                best = { u: u, el: el };
                            }
                        });
                        return best;
                    };
                    let scope = playBtn;
                    for (let i = 0; i < 10 && scope; i++) {
                        if (scope.querySelector && scope.querySelector('.player-info-text[data-type="title"]')) break;
                        scope = scope.parentElement;
                    }
                    let picked = null;
                    if (scope) picked = pickCoverIn(scope);
                    if (picked) {
                        coverUrl = picked.u;
                        if (picked.el.tagName && picked.el.tagName.toLowerCase() === 'img') {
                            coverImgEl = picked.el;
                        }
                    } else {
                        const els = document.querySelectorAll('img, div');
                        els.forEach(el => {
                            let u = '';
                            if (el.tagName && el.tagName.toLowerCase() === 'img') {
                                u = el.src;
                            } else {
                                const bg = window.getComputedStyle(el).backgroundImage;
                                if (bg && bg !== 'none' && bg.includes('url(')) {
                                    const match = bg.match(/url\\(["']?(.*?)["']?\\)/);
                                    if (match && match[1]) u = match[1];
                                }
                            }
                            if (u && !u.includes('.svg') && !u.includes('icon') && !u.includes('default')) {
                                const cn = (typeof el.className === 'string') ? el.className.toLowerCase() : '';
                                if (cn.includes('artwork') || cn.includes('cover') || cn.includes('album') || cn.includes('jacket') || cn.includes('player') || u.startsWith('blob:')) {
                                    coverUrl = u;
                                    if (el.tagName && el.tagName.toLowerCase() === 'img') {
                                        coverImgEl = el;
                                    }
                                }
                            }
                        });
                    }

                    const currentPos = parseFloat(position);
                    const timeSinceLastUpdate = (Date.now() - lastUpdateTime) / 1000;
                    const expectedPos = (lastState === 'playing') ? (lastPosition + timeSinceLastUpdate) : lastPosition;

                    const titleChanged = (title !== lastTitle);
                    const stateChanged = (isPlaying !== (lastState === 'playing'));
                    const coverChanged = (coverUrl !== lastCoverUrl) || titleChanged;
                    const seeked = Math.abs(currentPos - expectedPos) > 3;
                    const currentDur = parseFloat(duration);
                    const durationChanged = (currentDur > 0 && Math.abs(currentDur - lastDuration) > 1);

                    const sendUpdate = (covData) => {
                        const payload = { title: title, artist: artist, album: album, state: lastState, position: position, duration: duration, cover: covData };
                        payload.metaChanged = titleChanged || coverChanged;
                        console.log("SMTC_UPDATE|" + JSON.stringify(payload));
                    };

                    if (titleChanged || stateChanged || coverChanged || seeked || durationChanged) {
                        lastTitle = title;
                        lastState = isPlaying ? 'playing' : 'paused';
                        lastPosition = currentPos;
                        lastDuration = currentDur;
                        lastUpdateTime = Date.now();

                        if (coverChanged) {
                            lastCoverUrl = coverUrl;
                            if (coverUrl.startsWith('blob:') && coverImgEl) {
                                let b64 = getBase64Image(coverImgEl);
                                lastCoverData = b64 || '';
                                sendUpdate(lastCoverData);
                            } else {
                                lastCoverData = coverUrl;
                                sendUpdate(lastCoverData);
                            }
                        } else {
                            sendUpdate(lastCoverData);
                        }
                    }
                }, 500);
            })();
        `).catch(() => {});
    };

    wc.on('dom-ready', onReady);
    wc.on('did-finish-load', onReady);
});
