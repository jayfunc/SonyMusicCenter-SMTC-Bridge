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
        if (msg && msg.startsWith('SMTC_DUMP|')) {
            try {
                require('fs').writeFileSync(require('path').join(require('os').tmpdir(), 'smtc_dom_dump.html'), msg.substring(10));
            } catch(e) {}
        }
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
            } catch(e) {}
        }
    });

    const onReady = () => {
        const url = wc.getURL();
        if (url.includes('window-main')) {
            setInterval(() => {
                http.get('http://127.0.0.1:9999/poll', res => {
                    let data = '';
                    res.on('data', chunk => data += chunk);
                    res.on('end', () => {
                        if (!data) return;
                        let code = '';
                        if (data === 'play' || data === 'pause') {
                            code = "var b = document.getElementById('player-control-play-pause'); if(b) b.click();";
                        } else if (data === 'next') {
                            code = "var b = document.getElementById('player-control-next'); if(b) b.click();";
                        } else if (data === 'prev') {
                            code = "var b = document.getElementById('player-control-previous'); if(b) b.click();";
                        }
                        if (code) wc.executeJavaScript(code).catch(()=>{});
                    });
                }).on('error', () => {});
            }, 300);

            wc.executeJavaScript(`
                (function() {
                    if (window.__smtcBridgeHookLoaded) return;
                    window.__smtcBridgeHookLoaded = true;
                    
                    let lastTitle = '';
                    let lastState = '';
                    let lastPosition = 0;
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
                            position = (parseFloat(slider.value) / 1000).toString();
                            duration = (parseFloat(slider.max) / 1000).toString();
                        }

                        let coverUrl = '';
                        let coverImgEl = null;
                        const els = document.querySelectorAll('img, div');
                        els.forEach(el => {
                            let u = '';
                            if (el.tagName && el.tagName.toLowerCase() === 'img') {
                                u = el.src;
                            } else {
                                const bg = window.getComputedStyle(el).backgroundImage;
                                if (bg && bg !== 'none' && bg.includes('url(')) {
                                    const match = bg.match(/url\(["']?(.*?)["']?\)/);
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

                        const currentPos = parseFloat(position);
                        const timeSinceLastUpdate = (Date.now() - lastUpdateTime) / 1000;
                        const expectedPos = (lastState === 'playing') ? (lastPosition + timeSinceLastUpdate) : lastPosition;
                        
                        if (title !== lastTitle) {
                            console.log("SMTC_DUMP|" + document.body.innerHTML);
                        }
                        const titleChanged = (title !== lastTitle);
                        const stateChanged = (isPlaying !== (lastState === 'playing'));
                        const coverChanged = (coverUrl !== lastCoverUrl) || titleChanged;
                        const seeked = Math.abs(currentPos - expectedPos) > 3;

                        const sendUpdate = (covData) => {
                            const payload = { title: title, artist: artist, album: album, state: lastState, position: position, duration: duration, cover: covData };
                            payload.metaChanged = titleChanged || coverChanged;
                            console.log("SMTC_UPDATE|" + JSON.stringify(payload));
                        };

                        if (titleChanged || stateChanged || coverChanged || seeked) {
                            lastTitle = title;
                            lastState = isPlaying ? 'playing' : 'paused';
                            lastPosition = currentPos;
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
            `).catch(()=>{});
        }
    };
    
    wc.on('dom-ready', onReady);
    wc.on('did-finish-load', onReady);
});
